namespace EstagioCheck.API.Models;

public class RotationSchedule
{
    /// <summary>
    /// Limites de um período de rodízio plausível. Data vazia chega à API como
    /// 01/01/0001 e um ano digitado errado vira 1970, 2004…: nenhum dos dois é um
    /// estágio de verdade, e contá-los gerava milhares de dias "sem registro".
    /// </summary>
    public static readonly DateOnly DataMinima = new(2020, 1, 1);
    public static readonly DateOnly DataMaxima = new(2100, 12, 31);

    /// <summary>Um rodízio dura semanas ou meses; mais de um ano é erro de digitação.</summary>
    public const int DuracaoMaximaDias = 366;

    /// <summary>Período dentro dos limites e com término a partir do início.</summary>
    public static bool PeriodoValido(DateOnly inicio, DateOnly fim) =>
        inicio >= DataMinima && fim <= DataMaxima && fim >= inicio;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Guid LocationId { get; set; }
    public Guid? PreceptorId { get; set; }

    /// <summary>"manha" | "tarde" | "noite"</summary>
    public string Shift { get; set; } = "manha";

    public string PeriodLabel { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>"gestao" | "pic" | "assistencia" | "outro"</summary>
    public string ActivityType { get; set; } = "assistencia";

    public int RequiredHours { get; set; } = 80;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Programação por dia da semana. Vazia, o rodízio vale como antes: todo dia
    /// útil é presencial no local principal.
    /// </summary>
    public ICollection<RotationDaySchedule> Days { get; set; } = [];

    // Navigation
    public StudentGroup Group { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public ApplicationUser? Preceptor { get; set; }
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = [];
    public ICollection<FormativeFollowup> Followups { get; set; } = [];
    public ICollection<RemoteActivity> RemoteActivities { get; set; } = [];
}
