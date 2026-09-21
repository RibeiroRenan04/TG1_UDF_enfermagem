namespace EstagioCheck.API.Models;

/// <summary>
/// Vínculo de um aluno com uma turma.
///
/// O aluno pode ter mais de um vínculo ativo: em enfermagem é comum cursar dois
/// módulos de estágio ao mesmo tempo (Saúde Coletiva/UBS em um turno e Estágio
/// Hospitalar em outro) ou repor carga horária em uma turma complementar. Por
/// isso vincular a uma nova turma não desfaz o vínculo anterior — o par
/// (aluno, turma) é que é único.
/// </summary>
public class GroupMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudentId { get; set; }
    public Guid GroupId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ApplicationUser Student { get; set; } = null!;
    public StudentGroup Group { get; set; } = null!;
}
