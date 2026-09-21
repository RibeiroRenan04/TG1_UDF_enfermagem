using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>
/// Compatibilidade de agenda entre as turmas de um aluno.
///
/// Estar em duas turmas é normal — Saúde Coletiva pela manhã e Estágio Hospitalar
/// à tarde, ou uma turma de reposição em dias alternados. O que não existe é o
/// aluno em dois lugares ao mesmo tempo: mesmo turno, mesmo dia da semana e
/// períodos que se sobrepõem. Só essa sobreposição exata é recusada; qualquer
/// outra combinação passa.
/// </summary>
public class ConflitoTurmasService(AppDbContext db)
{
    /// <summary>
    /// Verifica se vincular o aluno à turma cria sobreposição com as turmas em que
    /// ele já está. Devolve a explicação do conflito ou <c>null</c> quando a
    /// agenda é compatível.
    /// </summary>
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

        var atuais = await EscalasAsync(outrasTurmas, ct);

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

    /// <summary>
    /// Dias da semana que o rodízio ocupa. Sem programação cadastrada ele vale
    /// como antes — segunda a sexta no local principal.
    /// </summary>
    private static HashSet<int> DiasOcupados(RotationSchedule escala) =>
        escala.Days.Count == 0
            ? [1, 2, 3, 4, 5]
            : [.. escala.Days
                .Where(d => ModoAtividade.Normalizar(d.Mode) != ModoAtividade.SemAtividade)
                .Select(d => d.DayOfWeek)];
}
