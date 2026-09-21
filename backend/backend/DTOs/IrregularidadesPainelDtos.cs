namespace EstagioCheck.API.DTOs;

/// <summary>
/// Indicadores das irregularidades de ponto para o professor e a coordenadora:
/// a fila que espera decisão, onde ela emperra, e os padrões que apontam causa
/// (tipo, unidade, aluno) — "fora do local" concentrado numa unidade costuma ser
/// coordenada ou raio errado, não aluno burlando o ponto.
/// </summary>
public class IrregularidadesPainelDto
{
    /// <summary>Janela analisada, em dias. Nulo quando é todo o histórico.</summary>
    public int? Dias { get; init; }

    // ── Fila atual (não depende do período) ───────────────────────────────────
    public int AguardandoProfessor { get; init; }
    /// <summary>Há quantos dias a ocorrência mais antiga espera a decisão do professor.</summary>
    public int? MaisAntigaAguardandoProfessorDias { get; init; }
    public int AguardandoPreceptor { get; init; }
    public int? MaisAntigaAguardandoPreceptorDias { get; init; }

    // ── Período ───────────────────────────────────────────────────────────────
    public int AbertasNoPeriodo { get; init; }
    /// <summary>Abertas na janela anterior de mesmo tamanho, para comparação. Nulo sem período.</summary>
    public int? AbertasPeriodoAnterior { get; init; }
    public int DecididasNoPeriodo { get; init; }
    /// <summary>Aprovadas sobre decididas no período (0–100). Nulo sem decisões.</summary>
    public double? TaxaAprovacao { get; init; }
    /// <summary>Dias, em média, da abertura até a ciência do preceptor.</summary>
    public double? MediaDiasPreceptor { get; init; }
    /// <summary>Dias, em média, da ciência (ou da abertura) até a decisão do professor.</summary>
    public double? MediaDiasProfessor { get; init; }

    /// <summary>"semana" ou "mes": como a evolução foi agrupada.</summary>
    public string Agrupamento { get; init; } = "semana";
    public List<IrregularidadesPeriodoDto> Evolucao { get; init; } = [];
    public List<IrregularidadesPorTipoDto> PorTipo { get; init; } = [];
    public List<IrregularidadesPorUnidadeDto> PorUnidade { get; init; } = [];
    public List<IrregularidadesPorAlunoDto> PorAluno { get; init; } = [];
    public List<FilaPreceptorDto> FilaPreceptores { get; init; } = [];
}

public class IrregularidadesPeriodoDto
{
    public DateOnly Inicio { get; init; }
    public string Rotulo { get; init; } = string.Empty;
    public int Abertas { get; init; }
}

public class IrregularidadesPorTipoDto
{
    public string Tipo { get; init; } = string.Empty;
    public string Rotulo { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Aprovadas { get; init; }
    public int Negadas { get; init; }
}

public class IrregularidadesPorUnidadeDto
{
    public Guid UnidadeId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public int Total { get; init; }
    /// <summary>Quantas são "registro fora do local" — sinal de coordenada ou raio errado.</summary>
    public int ForaDoLocal { get; init; }
}

public class IrregularidadesPorAlunoDto
{
    public Guid StudentId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? Rgm { get; init; }
    public int Total { get; init; }
    public int Negadas { get; init; }
}

public class FilaPreceptorDto
{
    /// <summary>Nulo quando o rodízio da ocorrência não tem preceptor definido.</summary>
    public Guid? PreceptorId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public int Pendentes { get; init; }
    public int MaisAntigaDias { get; init; }
}
