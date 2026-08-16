using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AppDbContext db, TokenService tokenService, EmailService emailService) : ControllerBase
{
    // O autocadastro foi removido: alunos são criados via importação e
    // preceptores/supervisores pelo cadastro do professor (UsersController).

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email.ToLower());
        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(new { message = "Credenciais inválidas." });

        var token = tokenService.GenerateToken(user);
        return Ok(new AuthResponseDto(token, user.Id.ToString(), user.Email, user.FullName, user.Role,
            user.MustChangePassword, user.MustSetEmail));
    }

    // ── Primeiro Acesso ───────────────────────────────────────────────────────
    [HttpPost("first-access")]
    [Authorize]
    public async Task<ActionResult<AuthResponseDto>> FirstAccess([FromBody] FirstAccessDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

        if (userId == null || !Guid.TryParse(userId, out var id))
            return Unauthorized();

        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (!user.MustChangePassword && !user.MustSetEmail)
            return BadRequest(new { message = "Primeiro acesso não necessário para este usuário." });

        // O e-mail institucional é exigido apenas do aluno. Preceptores costumam ser
        // profissionais externos à UDF e podem usar e-mail próprio.
        if (user.Role == "aluno" && !EhEmailInstitucional(dto.Email))
            return BadRequest(new { message = "O e-mail deve ser institucional (@cs.udf.edu.br)." });

        var emailTaken = await db.Users.AnyAsync(u => u.Email == dto.Email.ToLower() && u.Id != user.Id);
        if (emailTaken)
            return Conflict(new { message = "E-mail já cadastrado." });

        user.Email = dto.Email.Trim().ToLower();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.MustChangePassword = false;
        user.MustSetEmail = false;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        var token = tokenService.GenerateToken(user);
        return Ok(new AuthResponseDto(token, user.Id.ToString(), user.Email, user.FullName, user.Role));
    }

    // ── Esqueci a senha ───────────────────────────────────────────────────────
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        // Não há restrição de domínio aqui: preceptores externos precisam conseguir
        // recuperar a senha com o e-mail que usam para entrar.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email.ToLower());

        // Retorna 200 mesmo se o e-mail não existir para não revelar cadastros
        if (user == null)
            return Ok(new { message = "Se o e-mail estiver cadastrado, você receberá o código em breve." });

        var code = Random.Shared.Next(100000, 999999).ToString();

        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            Email = dto.Email.Trim().ToLower(),
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        });
        await db.SaveChangesAsync();

        await emailService.SendResetCodeAsync(user.Email!, code);

        return Ok(new { message = "Se o e-mail estiver cadastrado, você receberá o código em breve." });
    }

    // ── Verificar código ──────────────────────────────────────────────────────
    [HttpPost("verify-reset-code")]
    public async Task<IActionResult> VerifyResetCode([FromBody] VerifyResetCodeDto dto)
    {
        var record = await db.PasswordResetCodes.FirstOrDefaultAsync(r =>
            r.Email == dto.Email.ToLower() &&
            r.Code == dto.Code &&
            !r.Used &&
            r.ExpiresAt > DateTime.UtcNow);

        if (record == null)
            return BadRequest(new { message = "Código inválido ou expirado." });

        return Ok(new { message = "Código válido." });
    }

    // ── Redefinir senha ───────────────────────────────────────────────────────
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        var record = await db.PasswordResetCodes.FirstOrDefaultAsync(r =>
            r.Email == dto.Email.ToLower() &&
            r.Code == dto.Code &&
            !r.Used &&
            r.ExpiresAt > DateTime.UtcNow);

        if (record == null)
            return BadRequest(new { message = "Código inválido ou expirado." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email.ToLower());
        if (user == null)
            return NotFound(new { message = "Usuário não encontrado." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        record.Used = true;

        await db.SaveChangesAsync();

        return Ok(new { message = "Senha redefinida com sucesso." });
    }

    private const string DominioInstitucional = "@cs.udf.edu.br";

    private static bool EhEmailInstitucional(string email) =>
        email.Trim().EndsWith(DominioInstitucional, StringComparison.OrdinalIgnoreCase);
}
