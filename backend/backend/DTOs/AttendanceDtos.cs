using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

public record CreateAttendanceDto(
    [Required] double Latitude,
    [Required] double Longitude,
    [Required] string Type, // "check_in" | "check_out"
    Guid? ScheduleId,
    Guid? LocationId,
    // Descrição das atividades do turno. Obrigatória no check-out — é o registro
    // do que o aluno fez no estágio; a API recusa o fechamento sem ela.
    [MaxLength(4000)] string? ActivitiesDescription,
    string? PhotoBase64,
    double? AccuracyMeters // precisão do GPS em metros (opcional)
);

public record ValidateAttendanceDto(
    [Required] bool Approve,
    string? Reason
);

public class AttendanceRecordDto
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public DateTime RecordedAt { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? DistanceMeters { get; init; }
    public string? PhotoUrl { get; init; }
    public string? ActivitiesDescription { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? IrregularityReason { get; init; }
    public string? LocationName { get; init; }
    public Guid? ScheduleId { get; init; }
    public Guid? LocationId { get; init; }
    public string? ValidatedByName { get; init; }
    public DateTime? ValidatedAt { get; init; }

    /// <summary>Turno em que o ponto foi registrado ("manha" | "tarde" | "noite").</summary>
    public string Shift { get; init; } = string.Empty;

    // ── Irregularidade aberta sobre este ponto ────────────────────────────────
    // Alimenta a trava de duplicidade da tela: enquanto houver uma contestação em
    // andamento, o aluno não abre outra para o mesmo ponto.
    public Guid? IrregularityId { get; init; }
    public string? IrregularityStatus { get; init; }
    /// <summary>Contestação em andamento (ainda não negada pelo professor).</summary>
    public bool HasOpenIrregularity { get; init; }
}

/// <summary>
/// Situação do ponto do aluno no turno corrente. Uma única chamada diz o que a
/// tela de registro precisa saber: qual turno está valendo, o que já foi
/// registrado nele e qual ação ainda está liberada.
/// </summary>
public class ShiftPointStatusDto
{
    public string Shift { get; init; } = string.Empty;
    public string ShiftLabel { get; init; } = string.Empty;
    public DateOnly Date { get; init; }

    public Guid? CheckInId { get; init; }
    public DateTime? CheckInAt { get; init; }
    public Guid? CheckOutId { get; init; }
    public DateTime? CheckOutAt { get; init; }

    /// <summary>Ainda não há check-in neste turno.</summary>
    public bool CanCheckIn { get; init; }
    /// <summary>Há check-in e ainda não há check-out neste turno.</summary>
    public bool CanCheckOut { get; init; }
    /// <summary>Check-in e check-out já registrados: o turno está fechado.</summary>
    public bool ShiftClosed { get; init; }
    /// <summary>Motivo do bloqueio, quando nenhuma ação está liberada.</summary>
    public string? BlockedReason { get; init; }
}

public class PendencyDto
{
    public DateOnly PendencyDate { get; init; }
    public Guid? ScheduleId { get; init; }
    public string LocationName { get; init; } = string.Empty;
    public double ExpectedHours { get; init; }
}

public class ActiveScheduleDto
{
    public Guid ScheduleId { get; init; }
    public string Shift { get; init; } = string.Empty;
    public string PeriodLabel { get; init; } = string.Empty;
    public string ActivityType { get; init; } = string.Empty;
    public int RequiredHours { get; init; }
    public LocationDto Location { get; init; } = null!;
}
