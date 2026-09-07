using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

/// <summary>Exceção do calendário: a data em que a programação normal não vale.</summary>
public class ExcecaoCalendarioDto
{
    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string TypeLabel { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string ScopeLabel { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string? Shift { get; init; }

    public Guid? GroupId { get; init; }
    public string? GroupCode { get; init; }
    public Guid? ScheduleId { get; init; }
    public string? PeriodLabel { get; init; }
    public Guid? StudentId { get; init; }
    public string? StudentName { get; init; }
    public string? Course { get; init; }

    public Guid? LocationId { get; init; }
    public string? LocationName { get; init; }
    public Guid? RemoteActivityId { get; init; }
    public string? RemoteActivityTitle { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>A exceção dispensa o aluno de bater ponto naquele dia.</summary>
    public bool DispensaPonto { get; init; }

    public string? CreatedByName { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CriarExcecaoCalendarioDto(
    [Required(ErrorMessage = "Informe o tipo da exceção.")] string Type,
    [Required(ErrorMessage = "Informe a abrangência.")] string Scope,
    [Required(ErrorMessage = "Informe a data inicial.")] DateOnly StartDate,
    DateOnly? EndDate,
    string? Shift,
    Guid? GroupId,
    Guid? ScheduleId,
    Guid? StudentId,
    [MaxLength(150)] string? Course,
    Guid? LocationId,
    Guid? RemoteActivityId,
    [Required(ErrorMessage = "Descreva a exceção."), MaxLength(300)] string Description
);
