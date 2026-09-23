using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>
/// Em que unidade o aluno está, por turno e desde quando. Trocar de unidade encerra a alocação
/// e cria outra: o histórico nunca é sobrescrito.
/// </summary>
public class StudentAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LocationId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>Chave da regra de duplicidade: uma alocação ativa por aluno e turno.</summary>
    public string Shift { get; set; } = Turnos.Manha;

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool Ativo { get; set; } = true;

    public string? Observacao { get; set; }

    public Guid? CreatedById { get; set; }

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    public Location Location { get; set; } = null!;
    public ApplicationUser Student { get; set; } = null!;
    public ApplicationUser? CreatedBy { get; set; }
}
