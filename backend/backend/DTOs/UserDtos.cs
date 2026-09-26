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
    public bool AllowLateArrival { get; init; }
    public string? LateArrivalNote { get; init; }
    public bool MustChangePassword { get; init; }
    public bool MustSetEmail { get; init; }
    public DateTime? TermsAcceptedAt { get; init; }
    /// <summary>Turma principal (a primeira), para as telas que mostram uma só; a lista completa está em <see cref="Groups"/>.</summary>
    public Guid? GroupId { get; init; }
    public string? GroupCode { get; init; }
    public string? GroupName { get; init; }

    public List<UserGroupDto> Groups { get; init; } = [];
}

public class UserGroupDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>Turno comum dos rodízios da turma; nulo sem rodízio ou com turnos diferentes.</summary>
    public string? Shift { get; init; }
}

/// <summary><c>GroupIds</c> é a lista completa; <c>GroupId</c> (legado) vale como lista de uma turma.</summary>
public record AssignGroupDto(Guid? GroupId, List<Guid>? GroupIds = null);

public record LatePermissionDto(
    [Required] bool AllowLateArrival,
    [MaxLength(500)] string? Note
);

public record UpdateShiftDto(
    [Required, MaxLength(10)] string Shift  // "manha" | "tarde" | "noite"
);

public record CreateStaffDto(
    [Required, MinLength(2), MaxLength(200)] string FullName,
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MinLength(6), MaxLength(100)] string Password,
    [Required] string Role,   // "preceptor" | "supervisor" | "secretaria"
    [MaxLength(200)] string? Institution,
    [MaxLength(30)] string? Phone
);

public record BulkImportStudentDto(
    [Required, MaxLength(50)] string Rgm,
    [Required, MinLength(2), MaxLength(200)] string FullName,
    [Required] int Semester,
    [Required, MaxLength(10)] string Shift  // "manha" | "tarde" | "noite"
);

public record BulkImportRequestDto(
    [Required] List<BulkImportStudentDto> Students
);

/// <summary>A senha inicial é o RGM e por isso não trafega aqui.</summary>
public record ImportedStudentLoginDto(string FullName, string Rgm, string Email);

public record BulkImportResponseDto(
    int Imported,
    int Updated,
    List<string> Errors,
    List<ImportedStudentLoginDto> Logins
);

public record AdvanceSemesterResponseDto(int Advanced, int Graduated);
