namespace EstagioCheck.API.DTOs;

public class DashboardStatsDto
{
    public int Total { get; init; }
    public int Approved { get; init; }
    public int Irregular { get; init; }
    public int Pending { get; init; }
    public double Hours { get; init; }
    public int Required { get; init; }
    public int PendencyDays { get; init; }
    public double PendencyHours { get; init; }
    public List<PendencyDto> Pendencies { get; init; } = [];

    public int TotalStudents { get; init; }

    public IrregularityCountsDto Irregularities { get; init; } = new();

    public List<PendingStatusDto> PendingStatuses { get; init; } = [];

    /// <summary>Código da turma, ou os códigos separados por vírgula quando o aluno cursa mais de uma.</summary>
    public string? GroupCode { get; init; }
    public string? GroupName { get; init; }

    public List<UserGroupDto> Groups { get; init; } = [];

    /// <summary>Turno do aluno: "manha" | "tarde" | "noite".</summary>
    public string? Shift { get; init; }
}

public class IrregularityCountsDto
{
    public int AwaitingPreceptor { get; init; }
    public int AwaitingProfessor { get; init; }
    public int Approved { get; init; }
    public int Denied { get; init; }
    public int Total { get; init; }
    /// <summary>Ocorrências ainda em análise (com o preceptor ou com o professor).</summary>
    public int Open { get; init; }
}

public class PendingStatusDto
{
    /// <summary>Identificador do tipo de aviso, usado pela tela para o ícone e o link.</summary>
    public string Kind { get; init; } = string.Empty;
    /// <summary>"info" | "atencao" | "critico" — define a cor do aviso.</summary>
    public string Severity { get; init; } = "info";
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string? Link { get; init; }
    public string? LinkLabel { get; init; }
    /// <summary>Quantidade associada, quando o aviso agrega itens.</summary>
    public int Count { get; init; }
    /// <summary>Data de referência do aviso (prazo, ocorrência ou registro).</summary>
    public DateTime? ReferenceDate { get; init; }
}
