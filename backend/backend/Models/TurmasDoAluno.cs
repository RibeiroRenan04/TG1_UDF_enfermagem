namespace EstagioCheck.API.Models;

/// <summary>
/// Leitura resumida dos vínculos de turma de um aluno.
///
/// Desde que o aluno passou a poder cursar mais de um rodízio ao mesmo tempo,
/// "a turma do aluno" deixou de ser uma pergunta com resposta única. Onde a tela
/// ainda mostra uma turma só (menu lateral, certificado, busca por RGM), vale a
/// <see cref="Principal"/>; onde cabe a lista inteira, usam-se os demais
/// utilitários daqui — para nenhuma tela inventar a sua própria regra.
/// </summary>
public static class TurmasDoAluno
{
    /// <summary>
    /// Turma principal: a de vínculo mais antigo — aquela em que o aluno foi
    /// matriculado primeiro. Empate desempata pelo código da turma.
    /// </summary>
    public static GroupMembership? Principal(IEnumerable<GroupMembership>? vinculos) =>
        vinculos?
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Group?.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    /// <summary>Ids de todas as turmas do aluno — base das consultas de rodízio.</summary>
    public static List<Guid> Ids(IEnumerable<GroupMembership>? vinculos) =>
        [.. (vinculos ?? []).Select(m => m.GroupId).Distinct()];

    /// <summary>Vínculos em ordem de exibição: a principal primeiro.</summary>
    public static List<GroupMembership> Ordenados(IEnumerable<GroupMembership>? vinculos) =>
        [.. (vinculos ?? [])
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Group?.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)];

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
