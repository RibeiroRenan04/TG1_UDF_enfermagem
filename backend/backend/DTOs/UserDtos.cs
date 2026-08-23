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
    public Guid? GroupId { get; init; }
    public string? GroupCode { get; init; }
    public string? GroupName { get; init; }
}

public record AssignGroupDto(Guid? GroupId);

// ── Criação de preceptor / supervisor ─────────────────────────────────────────
public record CreateStaffDto(
    [Required, MinLength(2), MaxLength(200)] string FullName,
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MinLength(6), MaxLength(100)] string Password,
    [Required] string Role,   // "preceptor" | "supervisor"
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
