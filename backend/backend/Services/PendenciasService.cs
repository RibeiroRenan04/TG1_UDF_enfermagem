using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>
/// Dias passados sem registro de presença — fonte única do painel, dos relatórios e da tela
/// do preceptor. Só conta o dia que a programação exigia, entre o maior de (início do rodízio,
/// entrada do aluno na turma) e o menor de (ontem, fim do rodízio). Rodízio com período
/// inválido (01/01/0001, anos 1970…) é ignorado.
/// </summary>
public class PendenciasService(AppDbContext db, ProgramacaoService programacao)
{
    public async Task<List<PendencyDto>> CalcularAsync(Guid studentId, CancellationToken ct = default) =>
        (await CalcularLoteAsync([studentId], ct))[studentId];

    /// <summary>Pendências de vários alunos com um número fixo de consultas.</summary>
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

        // Uma janela por rodízio, no turno dele: quem cursa duas turmas é cobrado em cada turno.
        var janelasPorAluno = vinculos
            .SelectMany(v => escalas
                .Where(s => s.GroupId == v.GroupId)
                .Select(s => new
                {
                    v.StudentId,
                    Turno = Turnos.Normalizar(s.Shift)!,
                    Inicio = Maior(s.StartDate, DateOnly.FromDateTime(v.CreatedAt)),
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

        // Indexado por aluno, data e turno: só pela data, o check-in da manhã "cobria" a tarde.
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

            for (var data = fim; data >= inicio; data = data.AddDays(-1))
            {
                foreach (var turno in turnos)
                {
                    if (!janelas.Any(j => j.Turno == turno && data >= j.Inicio && data <= j.Fim)) continue;

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

    /// <summary>Janela do turno na unidade; em dia remoto, a carga da atividade; sem nenhuma, 8 horas.</summary>
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
