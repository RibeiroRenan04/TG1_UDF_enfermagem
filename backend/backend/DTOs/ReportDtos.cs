namespace EstagioCheck.API.DTOs;

/// <summary>Os totais usam a mesma conta do certificado e decidem a liberação.</summary>
public class ReportRowDto
{
    public Guid StudentId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Rgm { get; init; }
    public bool IsActive { get; init; }

    public int Required { get; init; }
    public double Hours { get; init; }
    public int Approved { get; init; }
    public int Irregular { get; init; }
    public int PendencyDays { get; init; }
    public double PendencyHours { get; init; }
    public double ProgressPercent { get; init; }
    public bool CertificateReleased { get; init; }

    /// <summary>Registros fora das turmas atuais (sem rodízio, ou de turma que o aluno deixou).</summary>
    public double HoursOutsideGroups { get; init; }

    public List<ReportTurmaDto> Turmas { get; init; } = [];
}

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
