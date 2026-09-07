namespace EstagioCheck.API.DTOs;

/// <summary>
/// Programação de um dia: onde o aluno deveria estar, o que deveria fazer e como
/// a presença daquele dia é comprovada. É a partir dela que a tela de ponto decide
/// se pede localização, se pede o código da atividade remota ou se não pede nada.
/// </summary>
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

    /// <summary>Existe ponto a registrar neste dia.</summary>
    public bool ExigePonto { get; init; }

    public Guid? ScheduleId { get; init; }
    public string? PeriodLabel { get; init; }
    public string? ActivityType { get; init; }

    /// <summary>Unidade do dia. Nula quando o dia é remoto ou sem atividade.</summary>
    public LocationDto? Location { get; init; }

    /// <summary>Explicação do dia, exibida ao aluno.</summary>
    public string? Motivo { get; init; }

    public Guid? ExcecaoId { get; init; }
    public string? TipoExcecao { get; init; }
    public string? TipoExcecaoLabel { get; init; }
    public string? AbrangenciaExcecao { get; init; }

    /// <summary>Atividades remotas do dia disponíveis para o aluno.</summary>
    public List<AtividadeRemotaAlunoDto> AtividadesRemotas { get; init; } = [];
}
