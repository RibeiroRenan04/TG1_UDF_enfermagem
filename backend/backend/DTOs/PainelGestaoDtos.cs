namespace EstagioCheck.API.DTOs;

/// <summary>
/// Painel do professor e da coordenadora: o que precisa de atenção hoje, a
/// tendência de presença, quem está ficando para trás e o que falta configurar.
/// </summary>
public class PainelGestaoDto
{
    public DateOnly Data { get; init; }
    public PresencaHojeDto Hoje { get; init; } = new();

    /// <summary>Presença dos últimos 14 dias encerrados (até ontem), dia a dia.</summary>
    public List<PresencaDiaDto> UltimosDias { get; init; } = [];

    /// <summary>Alunos com mais turnos sem registro no mesmo período, do mais ao menos.</summary>
    public List<AlunoSemRegistroDto> MaisTurnosSemRegistro { get; init; } = [];

    public ProgressoCargaDto ProgressoCarga { get; init; } = new();
    public int IrregularidadesAguardandoProfessor { get; init; }
    public int IrregularidadesAguardandoPreceptor { get; init; }

    /// <summary>Cadastro que impede o estágio de funcionar (turma sem rodízio, unidade sem localização…).</summary>
    public List<AlertaConfiguracaoDto> Configuracao { get; init; } = [];
}

public class PresencaHojeDto
{
    /// <summary>Turnos em que algum aluno deveria registrar ponto hoje.</summary>
    public int Esperados { get; init; }
    public int Registrados { get; init; }
    public int SemRegistro => Esperados - Registrados;
    public List<PresencaTurnoDto> PorTurno { get; init; } = [];

    /// <summary>Quem ainda não registrou o check-in de hoje (os primeiros da lista).</summary>
    public List<AlunoSemRegistroDto> AlunosSemRegistro { get; init; } = [];
}

public class PresencaTurnoDto
{
    public string Turno { get; init; } = string.Empty;
    public string Rotulo { get; init; } = string.Empty;
    public int Esperados { get; init; }
    public int Registrados { get; init; }
}

public class PresencaDiaDto
{
    public DateOnly Data { get; init; }
    public int Esperados { get; init; }
    public int Registrados { get; init; }

    /// <summary>Nulo quando ninguém tinha estágio no dia (fim de semana, feriado).</summary>
    public double? Percentual => Esperados == 0 ? null : Math.Round(100.0 * Registrados / Esperados, 1);
}

public class AlunoSemRegistroDto
{
    public Guid StudentId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? Rgm { get; init; }
    public string? Turma { get; init; }
    public string? Turno { get; init; }
    public string? Unidade { get; init; }
    /// <summary>No ranking do período: quantos turnos ficaram sem registro.</summary>
    public int Quantidade { get; init; }
}

public class ProgressoCargaDto
{
    /// <summary>Alunos por faixa de carga horária cumprida, da menor à maior.</summary>
    public List<FaixaProgressoDto> Faixas { get; init; } = [];
    public int Elegiveis { get; init; }
    /// <summary>Alunos ativos sem rodízio: não há carga exigida a comparar.</summary>
    public int SemCargaDefinida { get; init; }
}

public class FaixaProgressoDto
{
    public string Rotulo { get; init; } = string.Empty;
    public int Alunos { get; init; }
}

public class AlertaConfiguracaoDto
{
    public string Codigo { get; init; } = string.Empty;
    public string Titulo { get; init; } = string.Empty;
    public string Detalhe { get; init; } = string.Empty;
    public int Quantidade { get; init; }
    /// <summary>"critico" impede o ponto; "atencao" precisa de acerto, mas não trava nada.</summary>
    public string Severidade { get; init; } = "atencao";
    public string Link { get; init; } = string.Empty;
    public string LinkRotulo { get; init; } = string.Empty;
}
