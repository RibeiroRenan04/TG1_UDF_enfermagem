using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

public class GroupDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int MemberCount { get; init; }
}

public record CreateGroupDto(
    [Required, MaxLength(20)] string Code,
    [Required, MaxLength(200)] string Name,
    string? Description
);

public record VinculoLoteDto(List<Guid>? Adicionar, List<Guid>? Remover);

public record VinculoRecusadoDto(Guid StudentId, string? Nome, string Motivo);

public record VinculoLoteResultadoDto(int Vinculados, int Desvinculados, List<VinculoRecusadoDto> Recusados);

public class GroupMemberDto
{
    public Guid StudentId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Rgm { get; init; }
    public int? Semester { get; init; }
    public string? Shift { get; init; }
    public bool IsActive { get; init; }
}

public class ScheduleDto
{
    public Guid Id { get; init; }
    public Guid GroupId { get; init; }
    public string GroupCode { get; init; } = string.Empty;
    public string? GroupName { get; init; }
    public Guid LocationId { get; init; }
    public string LocationName { get; init; } = string.Empty;
    public Guid? PreceptorId { get; init; }
    public string? PreceptorName { get; init; }
    public string Shift { get; init; } = string.Empty;
    public string PeriodLabel { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string ActivityType { get; init; } = string.Empty;
    public int RequiredHours { get; init; }
    public string? Notes { get; init; }

    /// <summary>Vazia: todo dia útil é presencial no local principal.</summary>
    public List<DiaRodizioDto> Days { get; init; } = [];
}

public class DiaRodizioDto
{
    /// <summary>0 = domingo … 6 = sábado.</summary>
    public int DayOfWeek { get; init; }
    public string DayLabel { get; init; } = string.Empty;

    /// <summary>"presencial" | "remoto" | "sem_atividade".</summary>
    public string Mode { get; init; } = string.Empty;
    public string ModeLabel { get; init; } = string.Empty;

    public Guid? LocationId { get; init; }
    public string? LocationName { get; init; }
    public string? Notes { get; init; }
}

public record CriarDiaRodizioDto(
    [Range(0, 6, ErrorMessage = "Dia da semana inválido.")] int DayOfWeek,
    [Required(ErrorMessage = "Informe o tipo de atividade do dia.")] string Mode,
    Guid? LocationId,
    [MaxLength(300)] string? Notes
);

public record CreateScheduleDto(
    [Required(ErrorMessage = "Selecione a turma.")] Guid GroupId,
    [Required(ErrorMessage = "Selecione o local do estágio.")] Guid LocationId,
    [Required(ErrorMessage = "Informe o preceptor responsável.")] Guid? PreceptorId,
    [Required(ErrorMessage = "Informe o turno.")] string Shift,
    [Required(ErrorMessage = "Informe o período."), MaxLength(100)] string PeriodLabel,
    [Required(ErrorMessage = "Informe a data de início.")] DateOnly StartDate,
    [Required(ErrorMessage = "Informe a data de término.")] DateOnly EndDate,
    [Required(ErrorMessage = "Informe a atividade a ser desenvolvida.")] string ActivityType,
    [Range(1, 2000, ErrorMessage = "Carga horária deve estar entre 1 e 2000 horas.")] int RequiredHours,
    string? Notes,
    // Omitida, todo dia útil é presencial no local principal.
    List<CriarDiaRodizioDto>? Days = null
);
