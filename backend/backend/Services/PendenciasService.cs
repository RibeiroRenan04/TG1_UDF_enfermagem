using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>
/// Dias passados em que faltou o registro de presença do aluno.
///
/// Fonte única das pendências do painel, dos relatórios e da tela do preceptor —
/// cada uma calculava do seu jeito e divergia.
///
/// A varredura é dirigida pela programação: só vira pendência o dia que exigia
/// alguma coisa do aluno (feriado, recesso, fim de semana sem regra e dia remoto
/// já cumprido não entram). E a janela é estrita:
/// <list type="bullet">
///   <item>começa no maior entre o início do rodízio e a entrada do aluno na turma;</item>
///   <item>termina ontem, ou no fim do rodízio, o que vier antes;</item>
///   <item>rodízio com período inválido (data vazia, 01/01/0001, anos 1970…) é ignorado.</item>
/// </list>
/// Sem esses limites, uma escala com data de início inválida fazia o painel
/// contar milhares de dias sem registro.
/// </summary>
public class PendenciasService(AppDbContext db, ProgramacaoService programacao)
{
    public async Task<List<PendencyDto>> CalcularAsync(Guid studentId, CancellationToken ct = default)
    {
        var vinculos = await db.GroupMemberships
            .AsNoTracking()
            .Where(m => m.StudentId == studentId)
            .Select(m => new { m.GroupId, m.CreatedAt })
            .ToListAsync(ct);
        if (vinculos.Count == 0) return [];

        var groupIds = vinculos.Select(v => v.GroupId).Distinct().ToList();

        var escalas = (await db.RotationSchedules
                .AsNoTracking()
                .Where(s => groupIds.Contains(s.GroupId))
                .Select(s => new { s.GroupId, s.StartDate, s.EndDate })
                .ToListAsync(ct))
            .Where(s => RotationSchedule.PeriodoValido(s.StartDate, s.EndDate))
            .ToList();
        if (escalas.Count == 0) return [];

        // Cada turma tem a sua janela: começa no maior entre o início do rodízio e
        // a entrada do aluno naquela turma. Cursando duas, a varredura cobre da
        // primeira janela aberta até a última fechada — o dia que não exigia nada
        // de nenhuma delas é descartado adiante pela programação.
        // O vínculo é gravado em UTC; o dia do estágio é o de Brasília.
        var janelas = vinculos
            .Select(v => new
            {
                v.GroupId,
                Entrada = DateOnly.FromDateTime(BrasiliaTime.DeUtc(v.CreatedAt))
            })
            .SelectMany(v => escalas
                .Where(s => s.GroupId == v.GroupId)
                .Select(s => new { Inicio = Maior(s.StartDate, v.Entrada), Fim = s.EndDate }))
            .Where(j => j.Fim >= j.Inicio)
            .ToList();
        if (janelas.Count == 0) return [];

        var inicio = janelas.Min(j => j.Inicio);
        var fim = Menor(janelas.Max(j => j.Fim), BrasiliaTime.Hoje.AddDays(-1));
        if (fim < inicio) return [];

        // Check-ins existentes, indexados por data: o dia remoto gera o mesmo par
        // de registros do presencial, então a comparação vale para os dois.
        var desde = inicio.ToDateTime(TimeOnly.MinValue);
        var checkIns = (await db.AttendanceRecords
                .AsNoTracking()
                .Where(r => r.StudentId == studentId && r.Type == "check_in" && r.RecordedAt >= desde)
                .Select(r => r.RecordedAt)
                .ToListAsync(ct))
            .Select(DateOnly.FromDateTime)
            .ToHashSet();

        var dias = await programacao.ObterIntervaloAsync(studentId, inicio, fim, ct: ct);

        return [.. dias
            // Só o dia dentro de alguma janela conta: o rodízio de uma turma não
            // cobra presença de antes de o aluno entrar nela.
            .Where(dia => janelas.Any(j => dia.Data >= j.Inicio && dia.Data <= j.Fim))
            .Where(dia => dia.ExigePonto && !checkIns.Contains(dia.Data))
            .Select(dia => new PendencyDto
            {
                PendencyDate = dia.Data,
                ScheduleId = dia.ScheduleId,
                LocationName = dia.Local?.Name ?? ModoAtividade.Rotulo(dia.Modo),
                ExpectedHours = HorasEsperadas(dia)
            })
            .OrderByDescending(p => p.PendencyDate)];
    }

    /// <summary>
    /// Carga horária do dia. Vem da janela do turno da unidade; em dia remoto, da
    /// carga informada pelo professor na atividade. Sem nenhuma das duas, 8 horas.
    /// </summary>
    private static double HorasEsperadas(ProgramacaoService.ProgramacaoDia dia)
    {
        if (dia.Local != null
            && TimeSpan.TryParse(dia.Local.ShiftStart, out var inicio)
            && TimeSpan.TryParse(dia.Local.ShiftEnd, out var fim))
            return (fim - inicio).TotalHours;

        if (dia.AtividadesRemotas.Count > 0)
            return dia.AtividadesRemotas.Sum(a => a.EstimatedHours);

        return 8;
    }

    private static DateOnly Maior(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Menor(DateOnly a, DateOnly b) => a < b ? a : b;
}
