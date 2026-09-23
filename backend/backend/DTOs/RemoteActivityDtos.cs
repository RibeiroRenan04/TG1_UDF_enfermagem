using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

public class AtividadeRemotaDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid GroupId { get; init; }
    public string GroupCode { get; init; } = string.Empty;
    public string? GroupName { get; init; }
    public Guid? ScheduleId { get; init; }
    public string? PeriodLabel { get; init; }
    public Guid ProfessorId { get; init; }
    public string ProfessorName { get; init; } = string.Empty;
    public DateOnly ActivityDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public double EstimatedHours { get; init; }
    public string PresenceCode { get; init; } = string.Empty;
    public bool RequiresTask { get; init; }
    public string? TaskType { get; init; }
    public string? TaskTypeLabel { get; init; }
    public string? TaskInstructions { get; init; }
    public bool Ativo { get; init; }

    public bool Aberta { get; init; }

    /// <summary>Situação em uma palavra: "agendada" | "aberta" | "encerrada".</summary>
    public string Situacao { get; init; } = string.Empty;

    public int TotalParticipantes { get; init; }
    public int TotalAlunosGrupo { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Nunca traz o código de presença: o professor o entrega à parte.</summary>
public class AtividadeRemotaAlunoDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateOnly ActivityDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public double EstimatedHours { get; init; }
    public bool RequiresTask { get; init; }
    public string? TaskType { get; init; }
    public string? TaskTypeLabel { get; init; }
    public string? TaskInstructions { get; init; }

    public bool Aberta { get; init; }
    public string Situacao { get; init; } = string.Empty;

    public bool JaRegistrada { get; init; }
    public DateTime? RegistradaEm { get; init; }
}

public class ParticipacaoAtividadeDto
{
    public Guid Id { get; init; }
    public Guid RemoteActivityId { get; init; }
    public Guid StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string? StudentRgm { get; init; }
    public DateTime RegisteredAt { get; init; }
    public string? TaskResponse { get; init; }
}

public record CriarAtividadeRemotaDto(
    [Required(ErrorMessage = "Informe o título da atividade."), MaxLength(200)] string Title,
    string? Description,
    [Required(ErrorMessage = "Selecione a turma ou grupo autorizado.")] Guid GroupId,
    Guid? ScheduleId,
    [Required(ErrorMessage = "Informe a data da atividade.")] DateOnly ActivityDate,
    [Required(ErrorMessage = "Informe o horário de início.")] TimeOnly StartTime,
    [Required(ErrorMessage = "Informe o horário de término.")] TimeOnly EndTime,
    [Range(0.5, 24, ErrorMessage = "A carga horária deve estar entre 0,5 e 24 horas.")] double EstimatedHours,
    bool RequiresTask,
    string? TaskType,
    string? TaskInstructions
);

public record RegistrarPresencaRemotaDto(
    [Required(ErrorMessage = "Informe o código de presença.")] string Code,
    [MaxLength(4000)] string? TaskResponse
);

public class PresencaRemotaResultadoDto
{
    public Guid ParticipationId { get; init; }
    public Guid RemoteActivityId { get; init; }
    public string ActivityTitle { get; init; } = string.Empty;
    public DateTime RegisteredAt { get; init; }
    public double HorasCreditadas { get; init; }
    public string Message { get; init; } = string.Empty;
}
