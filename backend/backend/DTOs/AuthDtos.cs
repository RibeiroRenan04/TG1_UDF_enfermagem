using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

public record RegisterDto(
    [Required, MinLength(2), MaxLength(200)] string FullName,
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MinLength(6), MaxLength(100)] string Password,
    [MaxLength(50)] string? Matricula,
    [Required] string Role  // "aluno" | "preceptor" | "supervisor" | "coordenadora"
);

public record LoginDto(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record AuthResponseDto(
    string Token,
    string UserId,
    string? Email,
    string FullName,
    string Role,
    bool MustChangePassword = false,
    bool MustSetEmail = false,
    /// <summary>
    /// Perfis não-aluno precisam aceitar o termo de responsabilidade de acesso
    /// (não compartilhar a senha e responder pelas ações feitas com a conta)
    /// antes de usar o sistema.
    /// </summary>
    bool MustAcceptTerms = false
);

// ── Termo de responsabilidade ─────────────────────────────────────────────────
public record AcceptTermsDto(
    [Required] bool Accepted
);

// ── Primeiro Acesso ───────────────────────────────────────────────────────────
public record FirstAccessDto(
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MinLength(6), MaxLength(100)] string NewPassword
);

// ── Esqueci a senha ───────────────────────────────────────────────────────────
public record ForgotPasswordDto(
    [Required, EmailAddress, MaxLength(255)] string Email
);

public record VerifyResetCodeDto(
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MinLength(6), MaxLength(6)] string Code
);

public record ResetPasswordDto(
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MinLength(6), MaxLength(6)] string Code,
    [Required, MinLength(6), MaxLength(100)] string NewPassword
);
