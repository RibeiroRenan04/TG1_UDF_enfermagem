namespace EstagioCheck.API.DTOs;

public class ProgramacaoDiaDto
{
    public DateOnly Data { get; init; }
    public string Turno { get; init; } = string.Empty;
    public string TurnoLabel { get; init; } = string.Empty;

    /// <summary>"presencial" | "remoto" | "sem_atividade".</summary>
    public string Modo { get; init; } = string.Empty;
    public string ModoLabel { get; init; } = string.Empty;

    /// <summary>"localizacao" | "codigo" | "nenhuma".</summary>
    public string Validacao { get; init; } = string.Empty;

    public bool ExigePonto { get; init; }

    public Guid? ScheduleId { get; init; }
    public string? PeriodLabel { get; init; }
    public string? ActivityType { get; init; }

    public LocationDto? Location { get; init; }

    public string? Motivo { get; init; }

    public Guid? ExcecaoId { get; init; }
    public string? TipoExcecao { get; init; }
    public string? TipoExcecaoLabel { get; init; }
    public string? AbrangenciaExcecao { get; init; }

    public List<AtividadeRemotaAlunoDto> AtividadesRemotas { get; init; } = [];
}
