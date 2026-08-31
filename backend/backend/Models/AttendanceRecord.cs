using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

public class AttendanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudentId { get; set; }
    public Guid? ScheduleId { get; set; }
    public Guid? LocationId { get; set; }

    /// <summary>"check_in" | "check_out"</summary>
    public string Type { get; set; } = "check_in";

    /// <summary>Horário do registro no fuso de Brasília (GMT-3), o fuso oficial do estágio.</summary>
    public DateTime RecordedAt { get; set; } = BrasiliaTime.Agora;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? DistanceMeters { get; set; }
    public string? PhotoUrl { get; set; }
    public string? ActivitiesDescription { get; set; }

    /// <summary>"aprovado" | "irregular" | "pendente"</summary>
    public string Status { get; set; } = "pendente";

    public string? IrregularityReason { get; set; }
    public Guid? ValidatedById { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;

    // Navigation
    public ApplicationUser Student { get; set; } = null!;
    public RotationSchedule? Schedule { get; set; }
    public Location? Location { get; set; }
    public ApplicationUser? ValidatedBy { get; set; }
}
