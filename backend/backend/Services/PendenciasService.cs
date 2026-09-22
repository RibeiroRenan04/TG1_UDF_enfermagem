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
    public async Task<List<PendencyDto>> CalcularAsync(Guid studentId, CancellationToken ct = default) =>
        (await CalcularLoteAsync([studentId], ct))[studentId];

    /// <summary>
    /// Pendências de vários alunos com um número fixo de consultas. A tela do
    /// preceptor e o relatório calculavam aluno por aluno — umas dez consultas
    /// cada —, e com a turma inteira a resposta passava de minutos.
    /// </summary>
    public async Task<Dictionary<Guid, List<PendencyDto>>> CalcularLoteAsync(
        IReadOnlyCollection<Guid> studentIds, CancellationToken ct = default)
    {
        var resultado = studentIds.Distinct().ToDictionary(id => id, _ => new List<PendencyDto>());
        var ids = resultado.Keys.ToList();
        if (ids.Count == 0) return resultado;

        var vinculos = await db.GroupMemberships
            .AsNoTracking()
            .Where(m => ids.Contains(m.StudentId))
            .Select(m => new { m.StudentId, m.GroupId, m.CreatedAt })
            .ToListAsync(ct);
        if (vinculos.Count == 0) return resultado;

        var groupIds = vinculos.Select(v => v.GroupId).Distinct().ToList();

        var escalas = (await db.RotationSchedules
                .AsNoTracking()
                .Where(s => groupIds.Contains(s.GroupId))
                .Select(s => new { s.GroupId, s.StartDate, s.EndDate, s.Shift })
                .ToListAsync(ct))
            .Where(s => RotationSchedule.PeriodoValido(s.StartDate, s.EndDate) && Turnos.Normalizar(s.Shift) != null)
            .ToList();
        if (escalas.Count == 0) return resultado;

        // Cada rodízio tem a sua janela, no turno dele: começa no maior entre o
        // início do rodízio e a entrada do aluno naquela turma. Cursando duas turmas,
        // o aluno tem janelas em turnos diferentes, e cada turno é cobrado à parte.
        // O vínculo é gravado em UTC; o dia do estágio é o de Brasília.
        var janelasPorAluno = vinculos
            .SelectMany(v => escalas
                .Where(s => s.GroupId == v.GroupId)
                .Select(s => new
                {
                    v.StudentId,
                    Turno = Turnos.Normalizar(s.Shift)!,
                    Inicio = Maior(s.StartDate, DateOnly.FromDateTime(BrasiliaTime.DeUtc(v.CreatedAt))),
                    Fim = s.EndDate
                }))
            .Where(j => j.Fim >= j.Inicio)
            .GroupBy(j => j.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(j => (j.Turno, j.Inicio, j.Fim)).ToList());

        var ontem = BrasiliaTime.Hoje.AddDays(-1);
        var varreduras = janelasPorAluno
            .Select(kv => (Aluno: kv.Key,
                           Inicio: kv.Value.Min(j => j.Inicio),
                           Fim: Menor(kv.Value.Max(j => j.Fim), ontem)))
            .Where(v => v.Fim >= v.Inicio)
            .ToList();
        if (varreduras.Count == 0) return resultado;

        var de = varreduras.Min(v => v.Inicio);
        var ate = varreduras.Max(v => v.Fim);
        var alunos = varreduras.Select(v => v.Aluno).ToList();

        // Check-ins existentes, indexados por aluno, data e turno — o turno do
        // registro é o do rodízio vinculado; sem rodízio, o do horário (a mesma regra
        // do ponto e do painel do professor). Indexar só pela data deixava o check-in
        // da manhã "cobrir" o turno da tarde de quem cursa duas turmas. O dia remoto
        // gera o mesmo par de registros do presencial, então a comparação vale para os dois.
        var desde = de.ToDateTime(TimeOnly.MinValue);
        var checkIns = (await db.AttendanceRecords
                .AsNoTracking()
                .Where(r => alunos.Contains(r.StudentId) && r.Type == "check_in" && r.RecordedAt >= desde)
                .Select(r => new { r.StudentId, r.RecordedAt, Turno = r.Schedule != null ? r.Schedule.Shift : null })
                .ToListAsync(ct))
            .Select(r => (r.StudentId, DateOnly.FromDateTime(r.RecordedAt),
                          Turnos.Normalizar(r.Turno) ?? Turnos.DaHora(r.RecordedAt)))
            .ToHashSet();

        var lote = await programacao.CarregarLoteAsync(alunos, de, ate, ct);

        foreach (var (aluno, inicio, fim) in varreduras)
        {
            var janelas = janelasPorAluno[aluno];
            var turnos = Turnos.Validos.Where(t => janelas.Any(j => j.Turno == t)).ToList();
            var lista = resultado[aluno];

            // Do mais recente para o mais antigo, a ordem que as telas exibem.
            for (var data = fim; data >= inicio; data = data.AddDays(-1))
            {
                foreach (var turno in turnos)
                {
                    // Só o dia dentro de alguma janela do turno conta: o rodízio de uma
                    // turma não cobra presença de antes de o aluno entrar nela.
                    if (!janelas.Any(j => j.Turno == turno && data >= j.Inicio && data <= j.Fim)) continue;

                    // O dia resolvido no turno: com duas turmas, resolver sem turno
                    // dependia da hora em que a tela era aberta.
                    var dia = lote.NoTurno(aluno, data, turno);
                    if (dia is not { ExigePonto: true } || checkIns.Contains((aluno, data, turno))) continue;

                    lista.Add(new PendencyDto
                    {
                        PendencyDate = dia.Data,
                        ScheduleId = dia.ScheduleId,
                        LocationName = dia.Local?.Name ?? ModoAtividade.Rotulo(dia.Modo),
                        ExpectedHours = HorasEsperadas(dia, turno)
                    });
                }
            }
        }

        return resultado;
    }

    /// <summary>
    /// Carga horária do dia. Vem da janela do turno na unidade (a mesma do ponto);
    /// em dia remoto, da carga informada pelo professor na atividade. Sem nenhuma
    /// das duas, 8 horas.
    /// </summary>
    private static double HorasEsperadas(ProgramacaoService.ProgramacaoDia dia, string turno)
    {
        if (dia.Local != null
            && Turnos.JanelaNaUnidade(dia.Local.ShiftStart, dia.Local.ShiftEnd, turno) is var (inicio, fim))
            return (fim - inicio).TotalHours;

        if (dia.AtividadesRemotas.Count > 0)
            return dia.AtividadesRemotas.Sum(a => a.EstimatedHours);

        return 8;
    }

    private static DateOnly Maior(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Menor(DateOnly a, DateOnly b) => a < b ? a : b;
}
