namespace EstagioCheck.API.Models;

public class StudentGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Código curto do grupo, ex: "T01"</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<GroupMembership> Memberships { get; set; } = [];
    public ICollection<RotationSchedule> Schedules { get; set; } = [];
    public ICollection<RemoteActivity> RemoteActivities { get; set; } = [];
}
