using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

public class ApplicationUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>"aluno" | "preceptor" | "supervisor" | "secretaria"</summary>
    public string Role { get; set; } = Roles.Aluno;

    /// <summary>A própria matrícula do aluno.</summary>
    public string? Rgm { get; set; }

    /// <summary>7 ou 8.</summary>
    public int? Semester { get; set; }

    /// <summary>Turno: "manha" | "tarde" | "noite"</summary>
    public string? Shift { get; set; }

    public string? Phone { get; set; }
    public string? Institution { get; set; }

    /// <summary>A carga horária continua exigida; só evita a irregularidade de horário.</summary>
    public bool AllowLateArrival { get; set; } = false;

    public string? LateArrivalNote { get; set; }

    /// <summary>Aceite do termo de responsabilidade, exigido de todo perfil não-aluno.</summary>
    public DateTime? TermsAcceptedAt { get; set; }

    public bool MustChangePassword { get; set; } = false;
    public bool MustSetEmail { get; set; } = false;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    public ICollection<GroupMembership> GroupMemberships { get; set; } = [];
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = [];
    public ICollection<Evaluation> EvaluationsAsStudent { get; set; } = [];
    public ICollection<Evaluation> EvaluationsAsPreceptor { get; set; } = [];
    public ICollection<RotationSchedule> SchedulesAsPreceptor { get; set; } = [];
    public ICollection<FormativeFollowup> FollowupsAsStudent { get; set; } = [];
    public ICollection<FormativeFollowup> FollowupsAsPreceptor { get; set; } = [];
    public ICollection<PointIrregularity> IrregularitiesAsStudent { get; set; } = [];
}
