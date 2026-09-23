using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

public class RotationSchedule
{
    /// <summary>Data vazia chega como 01/01/0001 e ano errado vira 1970: nenhum é estágio de verdade.</summary>
    public static readonly DateOnly DataMinima = new(2020, 1, 1);
    public static readonly DateOnly DataMaxima = new(2100, 12, 31);

    // Mais de um ano é erro de digitação.
    public const int DuracaoMaximaDias = 366;

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
    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;

    /// <summary>Vazia: todo dia útil é presencial no local principal.</summary>
    public ICollection<RotationDaySchedule> Days { get; set; } = [];

    public StudentGroup Group { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public ApplicationUser? Preceptor { get; set; }
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = [];
    public ICollection<FormativeFollowup> Followups { get; set; } = [];
    public ICollection<RemoteActivity> RemoteActivities { get; set; } = [];
}
