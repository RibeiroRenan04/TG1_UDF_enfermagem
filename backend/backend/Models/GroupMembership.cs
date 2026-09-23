using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>O aluno pode estar em várias turmas: o par (aluno, turma) é que é único.</summary>
public class GroupMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudentId { get; set; }
    public Guid GroupId { get; set; }
    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;

    public ApplicationUser Student { get; set; } = null!;
    public StudentGroup Group { get; set; } = null!;
}
