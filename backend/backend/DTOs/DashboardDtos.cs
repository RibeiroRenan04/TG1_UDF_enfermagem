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

    /// <summary>
    /// Alunos ativos. Alimenta o contador do painel do professor — antes o campo
    /// não existia na resposta e a tela mostrava sempre zero.
    /// </summary>
    public int TotalStudents { get; init; }

    // ── Irregularidades ───────────────────────────────────────────────────────
    // O painel lê as ocorrências da mesma fonte da tela de irregularidades, então
    // o que o aluno acabou de enviar aparece aqui na carga seguinte.
    public IrregularityCountsDto Irregularities { get; init; } = new();

    /// <summary>
    /// Avisos centralizados do card "Status Pendentes": prazos, turnos em aberto e
    /// o andamento das irregularidades, em um único lugar.
    /// </summary>
    public List<PendingStatusDto> PendingStatuses { get; init; } = [];
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

/// <summary>Um aviso do card "Status Pendentes".</summary>
public class PendingStatusDto
{
    /// <summary>Identificador do tipo de aviso, usado pela tela para o ícone e o link.</summary>
    public string Kind { get; init; } = string.Empty;
    /// <summary>"info" | "atencao" | "critico" — define a cor do aviso.</summary>
    public string Severity { get; init; } = "info";
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    /// <summary>Rota do frontend que resolve o aviso.</summary>
    public string? Link { get; init; }
    public string? LinkLabel { get; init; }
    /// <summary>Quantidade associada, quando o aviso agrega itens.</summary>
    public int Count { get; init; }
    /// <summary>Data de referência do aviso (prazo, ocorrência ou registro).</summary>
    public DateTime? ReferenceDate { get; init; }
}
