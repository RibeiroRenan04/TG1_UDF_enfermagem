using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>
/// O que o preceptor enxerga: os rodízios que supervisiona, as unidades desses rodízios e os
/// alunos das turmas deles. Um aluno que cursa outra turma aparece, mas só com os pontos e as
/// ocorrências dos rodízios deste preceptor.
/// </summary>
public class EscopoPreceptorService(AppDbContext db)
{
    public sealed record Escopo(List<Guid> Rodizios, List<Guid> Locais, List<Guid> Alunos);

    public async Task<Escopo> CarregarAsync(Guid preceptorId, CancellationToken ct = default)
    {
        var rodizios = await db.RotationSchedules
            .Where(s => s.PreceptorId == preceptorId)
            .Select(s => new { s.Id, s.GroupId, s.LocationId })
            .ToListAsync(ct);

        var ids = rodizios.Select(r => r.Id).ToList();
        var grupos = rodizios.Select(r => r.GroupId).Distinct().ToList();

        var locaisDosDias = await db.RotationDaySchedules
            .Where(d => ids.Contains(d.ScheduleId) && d.LocationId != null)
            .Select(d => d.LocationId!.Value)
            .ToListAsync(ct);

        var alunos = await db.GroupMemberships
            .Where(m => grupos.Contains(m.GroupId))
            .Select(m => m.StudentId)
            .Distinct()
            .ToListAsync(ct);

        return new Escopo(
            ids,
            [.. rodizios.Select(r => r.LocationId).Concat(locaisDosDias).Distinct()],
            alunos);
    }

    /// <summary>Ponto sem rodízio só entra se for de um aluno dele, numa unidade dele.</summary>
    public static IQueryable<AttendanceRecord> FiltrarPontos(IQueryable<AttendanceRecord> query, Escopo e) =>
        query.Where(r =>
            (r.ScheduleId != null && e.Rodizios.Contains(r.ScheduleId.Value))
            || (r.ScheduleId == null && e.Alunos.Contains(r.StudentId)
                && r.LocationId != null && e.Locais.Contains(r.LocationId.Value)));

    /// <summary>
    /// A ocorrência vale pelo rodízio dela ou, sem rodízio, pelo ponto contestado. Sem nenhum dos
    /// dois (ocorrência avulsa), basta ser aluno dele.
    /// </summary>
    public static IQueryable<PointIrregularity> FiltrarOcorrencias(IQueryable<PointIrregularity> query, Escopo e) =>
        query.Where(i =>
            (i.ScheduleId != null && e.Rodizios.Contains(i.ScheduleId.Value))
            || (i.ScheduleId == null && i.AttendanceRecordId != null && (
                    (i.AttendanceRecord!.ScheduleId != null && e.Rodizios.Contains(i.AttendanceRecord.ScheduleId.Value))
                    || (i.AttendanceRecord.ScheduleId == null && e.Alunos.Contains(i.StudentId)
                        && i.AttendanceRecord.LocationId != null && e.Locais.Contains(i.AttendanceRecord.LocationId.Value))))
            || (i.ScheduleId == null && i.AttendanceRecordId == null && e.Alunos.Contains(i.StudentId)));

    public async Task<bool> AlcancaOcorrenciaAsync(Guid preceptorId, Guid irregularidadeId, CancellationToken ct = default)
    {
        var escopo = await CarregarAsync(preceptorId, ct);
        return await FiltrarOcorrencias(db.PointIrregularities.Where(i => i.Id == irregularidadeId), escopo)
            .AnyAsync(ct);
    }
}
