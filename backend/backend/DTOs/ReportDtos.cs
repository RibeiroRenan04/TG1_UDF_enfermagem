namespace EstagioCheck.API.DTOs;

/// <summary>
/// Linha do relatório: o aluno como um todo, com o detalhe de cada turma em
/// <see cref="Turmas"/>. Os totais somam as turmas mais os registros que não
/// pertencem a nenhuma delas — é a mesma conta do certificado, e é ela que decide
/// a liberação.
/// </summary>
public class ReportRowDto
{
    public Guid StudentId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Rgm { get; init; }
    public bool IsActive { get; init; }

    public int Required { get; init; }
    /// <summary>Horas aprovadas (pares check-in/check-out aprovados), como no certificado.</summary>
    public double Hours { get; init; }
    public int Approved { get; init; }
    public int Irregular { get; init; }
    public int PendencyDays { get; init; }
    public double PendencyHours { get; init; }
    public double ProgressPercent { get; init; }
    public bool CertificateReleased { get; init; }

    /// <summary>
    /// Horas aprovadas de registros sem turma atual do aluno — ponto sem rodízio ou
    /// de uma turma da qual ele já saiu. Entram no total, não em uma turma.
    /// </summary>
    public double HoursOutsideGroups { get; init; }

    public List<ReportTurmaDto> Turmas { get; init; } = [];
}

/// <summary>O aluno em uma das turmas: só os registros e as pendências dos rodízios dela.</summary>
public class ReportTurmaDto
{
    public Guid GroupId { get; init; }
    public string GroupCode { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    /// <summary>Turno da turma pelos rodízios; nulo sem rodízio ou com turnos diferentes.</summary>
    public string? Shift { get; init; }

    public int Required { get; init; }
    public double Hours { get; init; }
    public int Approved { get; init; }
    public int Irregular { get; init; }
    public int PendencyDays { get; init; }
    public double PendencyHours { get; init; }
    public double ProgressPercent { get; init; }
}
