namespace EstagioCheck.API.Models;

/// <summary>
/// Regra única para "a turma do aluno", que pode cursar mais de um rodízio. Onde a tela mostra
/// uma só, vale a <see cref="Principal"/>.
/// </summary>
public static class TurmasDoAluno
{
    /// <summary>A de vínculo mais antigo; empate pelo código.</summary>
    public static GroupMembership? Principal(IEnumerable<GroupMembership>? vinculos) =>
        vinculos?
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Group?.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    public static List<Guid> Ids(IEnumerable<GroupMembership>? vinculos) =>
        [.. (vinculos ?? []).Select(m => m.GroupId).Distinct()];

    /// <summary>Vínculos em ordem de exibição: a principal primeiro.</summary>
    public static List<GroupMembership> Ordenados(IEnumerable<GroupMembership>? vinculos) =>
        [.. (vinculos ?? [])
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Group?.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Turno único dos rodízios da turma; <c>null</c> sem rodízio ou com turnos diferentes.</summary>
    public static string? Turno(StudentGroup? turma)
    {
        var turnos = (turma?.Schedules ?? [])
            .Select(s => Turnos.Normalizar(s.Shift))
            .Where(t => t != null)
            .Distinct()
            .ToList();

        return turnos.Count == 1 ? turnos[0] : null;
    }

    /// <summary>
    /// Turmas que ainda contam para o aluno: sem rodízio (aguardando alocação) ou com rodízio que
    /// termina hoje ou depois. Se todas já encerraram, mostra todas para o aluno não ficar sem turma.
    /// </summary>
    public static List<GroupMembership> Vigentes(IEnumerable<GroupMembership>? vinculos, DateOnly hoje)
    {
        var todos = Ordenados(vinculos);
        var vigentes = todos
            .Where(m => m.Group == null || m.Group.Schedules.Count == 0 || m.Group.Schedules.Any(s => s.EndDate >= hoje))
            .ToList();
        return vigentes.Count > 0 ? vigentes : todos;
    }

    /// <summary>Códigos das turmas em um rótulo só: "T01, T02". Nulo sem vínculo.</summary>
    public static string? Codigos(IEnumerable<GroupMembership>? vinculos)
    {
        var codigos = Ordenados(vinculos)
            .Select(m => m.Group?.Code)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();

        return codigos.Count == 0 ? null : string.Join(", ", codigos);
    }
}
