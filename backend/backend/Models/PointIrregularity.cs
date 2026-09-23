using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>O preceptor só toma ciência e observa; quem decide é o professor.</summary>
public class PointIrregularity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StudentId { get; set; }

    public Guid? AttendanceRecordId { get; set; }

    public Guid? ScheduleId { get; set; }

    /// <summary>
    /// "atraso" | "esquecimento_checkin" | "esquecimento_checkout" |
    /// "fora_do_local" | "falta_justificada" | "problema_tecnico" | "outro"
    /// </summary>
    public string Type { get; set; } = "outro";

    /// <summary>Data em que a ocorrência aconteceu (não a data do registro).</summary>
    public DateOnly OccurredOn { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// "aguardando_preceptor" | "aguardando_professor" | "aprovada" | "negada"
    /// </summary>
    public string Status { get; set; } = StatusAguardandoPreceptor;

    public Guid? PreceptorId { get; set; }
    public string? PreceptorNote { get; set; }
    public DateTime? PreceptorAcknowledgedAt { get; set; }

    public Guid? ProfessorId { get; set; }
    public string? ProfessorNote { get; set; }
    public DateTime? ProfessorDecidedAt { get; set; }

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    public ApplicationUser Student { get; set; } = null!;
    public AttendanceRecord? AttendanceRecord { get; set; }
    public RotationSchedule? Schedule { get; set; }
    public ApplicationUser? Preceptor { get; set; }
    public ApplicationUser? Professor { get; set; }

    public const string StatusAguardandoPreceptor = "aguardando_preceptor";
    public const string StatusAguardandoProfessor = "aguardando_professor";
    public const string StatusAprovada = "aprovada";
    public const string StatusNegada = "negada";

    public static readonly string[] TiposValidos =
    [
        "atraso", "esquecimento_checkin", "esquecimento_checkout",
        "fora_do_local", "falta_justificada", "problema_tecnico", "outro"
    ];

    /// <summary>Uma vez decidida pelo professor, a ocorrência não volta ao fluxo.</summary>
    public bool Decidida => Status is StatusAprovada or StatusNegada;
}
