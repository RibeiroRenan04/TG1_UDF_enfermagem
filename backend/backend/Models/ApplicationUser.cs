namespace EstagioCheck.API.Models;

/// <summary>Usuário do sistema (aluno, preceptor, professor/supervisor ou coordenadora).</summary>
public class ApplicationUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>"aluno" | "preceptor" | "supervisor" | "coordenadora"</summary>
    public string Role { get; set; } = Roles.Aluno;

    /// <summary>Registro Geral de Matriculado (RGM) – é a própria matrícula do aluno.</summary>
    public string? Rgm { get; set; }

    /// <summary>Semestre atual do aluno (7 ou 8).</summary>
    public int? Semester { get; set; }

    /// <summary>Turno: "manha" | "tarde" | "noite"</summary>
    public string? Shift { get; set; }

    public string? Phone { get; set; }
    public string? Institution { get; set; }

    /// <summary>
    /// Autoriza o aluno a chegar depois do horário previsto de início do estágio.
    /// A carga horária do dia continua sendo exigida: a permissão só evita que o
    /// registro tardio seja tratado como irregularidade de horário.
    /// </summary>
    public bool AllowLateArrival { get; set; } = false;

    /// <summary>Motivo da autorização de atraso, registrado pelo professor.</summary>
    public string? LateArrivalNote { get; set; }

    /// <summary>
    /// Aceite do termo de responsabilidade de acesso (não compartilhar a senha e
    /// responder pelas ações feitas com a conta). Exigido de todo perfil não-aluno.
    /// </summary>
    public DateTime? TermsAcceptedAt { get; set; }

    public bool MustChangePassword { get; set; } = false;
    public bool MustSetEmail { get; set; } = false;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public GroupMembership? GroupMembership { get; set; }
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = [];
    public ICollection<Evaluation> EvaluationsAsStudent { get; set; } = [];
    public ICollection<Evaluation> EvaluationsAsPreceptor { get; set; } = [];
    public ICollection<RotationSchedule> SchedulesAsPreceptor { get; set; } = [];
    public ICollection<FormativeFollowup> FollowupsAsStudent { get; set; } = [];
    public ICollection<FormativeFollowup> FollowupsAsPreceptor { get; set; } = [];
    public ICollection<PointIrregularity> IrregularitiesAsStudent { get; set; } = [];
}
