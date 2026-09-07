using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

/// <summary>Atividade remota como o professor a enxerga, com o código de presença.</summary>
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

    /// <summary>A janela do código está aberta agora.</summary>
    public bool Aberta { get; init; }

    /// <summary>Situação em uma palavra: "agendada" | "aberta" | "encerrada".</summary>
    public string Situacao { get; init; } = string.Empty;

    public int TotalParticipantes { get; init; }
    public int TotalAlunosGrupo { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Atividade remota como o aluno a enxerga. Nunca traz o código de presença: é
/// justamente ele que o professor entrega à parte para comprovar o acesso.
/// </summary>
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

    /// <summary>A janela está aberta e o código é aceito agora.</summary>
    public bool Aberta { get; init; }
    public string Situacao { get; init; } = string.Empty;

    /// <summary>O aluno já registrou participação nesta atividade.</summary>
    public bool JaRegistrada { get; init; }
    public DateTime? RegistradaEm { get; init; }
}

/// <summary>Participação de um aluno, exibida ao professor.</summary>
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

/// <summary>
/// Registro de presença remota. O código comprova o acesso dentro do prazo; a
/// resposta, quando a atividade exige tarefa, comprova a realização.
/// </summary>
public record RegistrarPresencaRemotaDto(
    [Required(ErrorMessage = "Informe o código de presença.")] string Code,
    [MaxLength(4000)] string? TaskResponse
);

/// <summary>Resposta do registro de presença remota.</summary>
public class PresencaRemotaResultadoDto
{
    public Guid ParticipationId { get; init; }
    public Guid RemoteActivityId { get; init; }
    public string ActivityTitle { get; init; } = string.Empty;
    public DateTime RegisteredAt { get; init; }
    public double HorasCreditadas { get; init; }
    public string Message { get; init; } = string.Empty;
}
