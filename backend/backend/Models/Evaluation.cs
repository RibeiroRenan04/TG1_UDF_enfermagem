using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

public class Evaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudentId { get; set; }
    public Guid PreceptorId { get; set; }
    public Guid? ScheduleId { get; set; }

    /// <summary>Nota de 1 a 5</summary>
    public short ActivitiesScore { get; set; }
    public short PostureScore { get; set; }
    public short PlanningScore { get; set; }

    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;

    public ApplicationUser Student { get; set; } = null!;
    public ApplicationUser Preceptor { get; set; } = null!;
    public RotationSchedule? Schedule { get; set; }
}
