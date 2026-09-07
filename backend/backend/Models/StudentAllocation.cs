using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>
/// Alocação de um estagiário a uma unidade de saúde, com histórico.
///
/// Convive com o rodízio da turma: o rodízio define a escala do grupo, enquanto
/// esta alocação registra, aluno a aluno, em que unidade ele está e desde quando.
/// Trocar de unidade encerra a alocação atual (DataFim) e cria outra — o histórico
/// nunca é sobrescrito.
///
/// A alocação é <b>por turno</b>: o mesmo aluno pode estar de manhã em uma unidade
/// e à tarde em outra, mas nunca ter duas alocações ativas no mesmo turno.
/// </summary>
public class StudentAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LocationId { get; set; }

    /// <summary>Usuário com papel "aluno". A API recusa qualquer outro perfil.</summary>
    public Guid StudentId { get; set; }

    /// <summary>
    /// Turno da alocação: "manha" | "tarde" | "noite". É a chave da regra de
    /// duplicidade — um aluno tem, no máximo, uma alocação ativa por turno.
    /// </summary>
    public string Shift { get; set; } = Turnos.Manha;

    public DateOnly StartDate { get; set; }

    /// <summary>Preenchida ao encerrar a alocação; nula enquanto ela está em vigor.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Alocação em vigor. Só pode haver uma ativa por aluno e turno.</summary>
    public bool Ativo { get; set; } = true;

    public string? Observacao { get; set; }

    /// <summary>Quem criou a alocação (professor/supervisor).</summary>
    public Guid? CreatedById { get; set; }

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    // Navigation
    public Location Location { get; set; } = null!;
    public ApplicationUser Student { get; set; } = null!;
    public ApplicationUser? CreatedBy { get; set; }
}
