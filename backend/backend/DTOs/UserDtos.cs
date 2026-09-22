using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

public class UserDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Rgm { get; init; }
    public int? Semester { get; init; }
    public string? Shift { get; init; }
    public string Role { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    /// <summary>Aluno autorizado a chegar após o horário previsto de início.</summary>
    public bool AllowLateArrival { get; init; }
    public string? LateArrivalNote { get; init; }
    public bool MustChangePassword { get; init; }
    public bool MustSetEmail { get; init; }
    /// <summary>Quando o usuário aceitou o termo de responsabilidade de acesso.</summary>
    public DateTime? TermsAcceptedAt { get; init; }
    /// <summary>
    /// Turma principal — a primeira em que o aluno entrou. Continua aqui porque
    /// várias telas mostram uma turma só; a lista completa está em
    /// <see cref="Groups"/>.
    /// </summary>
    public Guid? GroupId { get; init; }
    public string? GroupCode { get; init; }
    public string? GroupName { get; init; }

    /// <summary>
    /// Todas as turmas do aluno. São várias quando ele cursa mais de um módulo de
    /// estágio no mesmo período ou repõe carga horária em turma complementar.
    /// </summary>
    public List<UserGroupDto> Groups { get; init; } = [];
}

/// <summary>Turma do aluno, na forma enxuta que as telas de vínculo consomem.</summary>
public class UserGroupDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Turno da turma pelos seus rodízios ("manha" | "tarde" | "noite"). Nulo sem
    /// rodízio ou com rodízios em turnos diferentes. Vem no login e no painel do
    /// aluno, que são as telas que exibem a turma com o turno.
    /// </summary>
    public string? Shift { get; init; }
}

/// <summary>
/// Turmas do aluno. <c>GroupIds</c> define a lista completa; <c>GroupId</c>
/// continua aceito e vale como lista de uma turma só.
/// </summary>
public record AssignGroupDto(Guid? GroupId, List<Guid>? GroupIds = null);

// ── Permissão de atraso ───────────────────────────────────────────────────────
public record LatePermissionDto(
    [Required] bool AllowLateArrival,
    [MaxLength(500)] string? Note
);

// ── Troca de turno do aluno ───────────────────────────────────────────────────
public record UpdateShiftDto(
    [Required, MaxLength(10)] string Shift  // "manha" | "tarde" | "noite"
);

// ── Criação de preceptor / supervisor ─────────────────────────────────────────
public record CreateStaffDto(
    [Required, MinLength(2), MaxLength(200)] string FullName,
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MinLength(6), MaxLength(100)] string Password,
    [Required] string Role,   // "preceptor" | "supervisor" | "coordenadora"
    [MaxLength(200)] string? Institution,
    [MaxLength(30)] string? Phone
);

// ── Importação em lote de alunos ──────────────────────────────────────────────
public record BulkImportStudentDto(
    [Required, MaxLength(50)] string Rgm,
    [Required, MinLength(2), MaxLength(200)] string FullName,
    [Required] int Semester,
    [Required, MaxLength(10)] string Shift  // "manha" | "tarde" | "noite"
);

public record BulkImportRequestDto(
    [Required] List<BulkImportStudentDto> Students
);

/// <summary>
/// Login gerado para um aluno na importação. A senha inicial é o próprio RGM e
/// por isso não trafega aqui: quem importou já enviou os RGMs na planilha.
/// </summary>
public record ImportedStudentLoginDto(string FullName, string Rgm, string Email);

public record BulkImportResponseDto(
    int Imported,
    int Updated,
    List<string> Errors,
    List<ImportedStudentLoginDto> Logins
);

// ── Avançar semestre ──────────────────────────────────────────────────────────
public record AdvanceSemesterResponseDto(int Advanced, int Graduated);
