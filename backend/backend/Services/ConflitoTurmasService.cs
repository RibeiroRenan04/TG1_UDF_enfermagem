using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>
/// Estar em duas turmas é normal; só é recusada a sobreposição exata: mesmo turno, mesmo dia
/// da semana e períodos que se sobrepõem.
/// </summary>
public class ConflitoTurmasService(AppDbContext db)
{
    /// <summary>Explicação do conflito, ou <c>null</c> quando a agenda é compatível.</summary>
    public async Task<string?> VerificarAsync(Guid studentId, Guid groupId, CancellationToken ct = default)
    {
        var novas = await EscalasAsync([groupId], ct);
        if (novas.Count == 0) return null;

        var outrasTurmas = await db.GroupMemberships
            .AsNoTracking()
            .Where(m => m.StudentId == studentId && m.GroupId != groupId)
            .Select(m => m.GroupId)
            .Distinct()
            .ToListAsync(ct);
        if (outrasTurmas.Count == 0) return null;

        return PrimeiroConflito(novas, await EscalasAsync(outrasTurmas, ct));
    }

    /// <summary>Mesma verificação para vários alunos em três consultas no total; devolve só os com conflito.</summary>
    public async Task<Dictionary<Guid, string>> VerificarLoteAsync(
        Guid groupId, IReadOnlyCollection<Guid> studentIds, CancellationToken ct = default)
    {
        var conflitos = new Dictionary<Guid, string>();
        if (studentIds.Count == 0) return conflitos;

        var novas = await EscalasAsync([groupId], ct);
        if (novas.Count == 0) return conflitos;

        var outrasPorAluno = (await db.GroupMemberships
                .AsNoTracking()
                .Where(m => studentIds.Contains(m.StudentId) && m.GroupId != groupId)
                .Select(m => new { m.StudentId, m.GroupId })
                .ToListAsync(ct))
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.GroupId).ToHashSet());
        if (outrasPorAluno.Count == 0) return conflitos;

        var escalasDasOutras = (await EscalasAsync([.. outrasPorAluno.Values.SelectMany(g => g).Distinct()], ct))
            .GroupBy(s => s.GroupId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (alunoId, turmas) in outrasPorAluno)
        {
            var atuais = turmas.SelectMany(t => escalasDasOutras.GetValueOrDefault(t) ?? []).ToList();
            var motivo = PrimeiroConflito(novas, atuais);
            if (motivo != null) conflitos[alunoId] = motivo;
        }

        return conflitos;
    }

    private static string? PrimeiroConflito(List<RotationSchedule> novas, List<RotationSchedule> atuais)
    {
        foreach (var nova in novas)
        {
            var turnoNovo = Turnos.Normalizar(nova.Shift);
            var diasNovos = DiasOcupados(nova);

            foreach (var atual in atuais)
            {
                if (Turnos.Normalizar(atual.Shift) != turnoNovo) continue;
                if (nova.StartDate > atual.EndDate || nova.EndDate < atual.StartDate) continue;

                var coincidem = diasNovos.Intersect(DiasOcupados(atual)).OrderBy(d => d).ToList();
                if (coincidem.Count == 0) continue;

                var inicio = nova.StartDate > atual.StartDate ? nova.StartDate : atual.StartDate;
                var fim = nova.EndDate < atual.EndDate ? nova.EndDate : atual.EndDate;

                return $"Conflito de agenda: o aluno já cursa o rodízio da turma "
                     + $"{atual.Group?.Code} no turno da {Turnos.Rotulo(turnoNovo ?? atual.Shift).ToLower()} "
                     + $"em {string.Join(", ", coincidem.Select(DiasSemana.Rotulo))}, "
                     + $"entre {inicio:dd/MM/yyyy} e {fim:dd/MM/yyyy}. "
                     + "Ajuste o turno, o período ou os dias da semana de um dos rodízios "
                     + "antes de vincular o aluno às duas turmas.";
            }
        }

        return null;
    }

    private async Task<List<RotationSchedule>> EscalasAsync(List<Guid> groupIds, CancellationToken ct) =>
        await db.RotationSchedules
            .AsNoTracking()
            .Include(s => s.Group)
            .Include(s => s.Days)
            .Where(s => groupIds.Contains(s.GroupId))
            .ToListAsync(ct);

    /// <summary>Sem programação cadastrada, segunda a sexta.</summary>
    private static HashSet<int> DiasOcupados(RotationSchedule escala) =>
        escala.Days.Count == 0
            ? [1, 2, 3, 4, 5]
            : [.. escala.Days
                .Where(d => ModoAtividade.Normalizar(d.Mode) != ModoAtividade.SemAtividade)
                .Select(d => d.DayOfWeek)];
}
