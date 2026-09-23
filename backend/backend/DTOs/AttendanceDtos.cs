using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

public record CreateAttendanceDto(
    [Required] double Latitude,
    [Required] double Longitude,
    [Required] string Type, // "check_in" | "check_out"
    Guid? ScheduleId,
    Guid? LocationId,
    // Obrigatória no check-out.
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

    public string Shift { get; init; } = string.Empty;

    // Preenchidos, o ponto veio do código de presença e não do geofence.
    public Guid? RemoteActivityId { get; init; }
    public string? RemoteActivityTitle { get; init; }

    public Guid? IrregularityId { get; init; }
    public string? IrregularityStatus { get; init; }
    /// <summary>Contestação em andamento (ainda não negada pelo professor).</summary>
    public bool HasOpenIrregularity { get; init; }
}

/// <summary>Turno valendo, o que já foi registrado nele e a ação ainda liberada.</summary>
public class ShiftPointStatusDto
{
    public string Shift { get; init; } = string.Empty;
    public string ShiftLabel { get; init; } = string.Empty;
    public DateOnly Date { get; init; }

    public Guid? CheckInId { get; init; }
    public DateTime? CheckInAt { get; init; }
    public Guid? CheckOutId { get; init; }
    public DateTime? CheckOutAt { get; init; }

    public bool CanCheckIn { get; init; }
    public bool CanCheckOut { get; init; }
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

    /// <summary>Modo do dia: "presencial" | "remoto" | "sem_atividade".</summary>
    public string Mode { get; init; } = string.Empty;
    public string ModeLabel { get; init; } = string.Empty;

    /// <summary>Como a presença do dia é comprovada: "localizacao" | "codigo" | "nenhuma".</summary>
    public string Validation { get; init; } = string.Empty;

    public string? Reason { get; init; }

    /// <summary>Unidade do dia. Nula em dia remoto ou sem atividade.</summary>
    public LocationDto? Location { get; init; }
}
