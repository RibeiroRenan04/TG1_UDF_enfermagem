using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>Código OTP de 6 dígitos para recuperação de senha.</summary>
public class PasswordResetCode
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; } = false;
    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
}
