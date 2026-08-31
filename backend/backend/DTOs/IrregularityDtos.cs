using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

/// <summary>Registro de irregularidade feito pelo aluno.</summary>
public record CreateIrregularityDto(
    [Required, MaxLength(30)] string Type,
    [Required] DateOnly OccurredOn,
    [Required, MinLength(10), MaxLength(2000)] string Description,
    Guid? AttendanceRecordId,
    Guid? ScheduleId
);

/// <summary>
/// Ciência do preceptor. Ele não decide a situação: apenas confirma que tomou
/// conhecimento, pode deixar uma observação e encaminha ao professor.
/// </summary>
public record PreceptorReviewIrregularityDto(
    [MaxLength(2000)] string? Note
);

/// <summary>Decisão do professor: aprovar ou negar, com parecer opcional.</summary>
public record ProfessorDecisionIrregularityDto(
    [Required] bool Approve,
    [MaxLength(2000)] string? Note
);

public class IrregularityDto
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string? StudentRgm { get; init; }
    public Guid? AttendanceRecordId { get; init; }
    public Guid? ScheduleId { get; init; }
    public string Type { get; init; } = string.Empty;
    public DateOnly OccurredOn { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    public Guid? PreceptorId { get; init; }
    public string? PreceptorName { get; init; }
    public string? PreceptorNote { get; init; }
    public DateTime? PreceptorAcknowledgedAt { get; init; }

    public Guid? ProfessorId { get; init; }
    public string? ProfessorName { get; init; }
    public string? ProfessorNote { get; init; }
    public DateTime? ProfessorDecidedAt { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
