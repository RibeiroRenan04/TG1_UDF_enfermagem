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
    // Sem autocadastro: alunos vêm da importação e os demais perfis, do cadastro do professor.

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        var user = await db.Users
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group).ThenInclude(g => g.Schedules)
            .FirstOrDefaultAsync(u => u.Email == dto.Email.ToLower());
        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(new { message = "Credenciais inválidas." });

        return Ok(Resposta(user));
    }

    private AuthResponseDto Resposta(ApplicationUser user) => new(
        tokenService.GenerateToken(user), user.Id.ToString(), user.Email, user.FullName, user.Role,
        user.MustChangePassword, user.MustSetEmail, DeveAceitarTermo(user),
        TurmasDoAluno.Vigentes(user.GroupMemberships, BrasiliaTime.Hoje).FirstOrDefault()?.Group?.Code,
        TurmasDoAluno.Vigentes(user.GroupMemberships, BrasiliaTime.Hoje).FirstOrDefault()?.Group?.Name,
        user.Shift,
        [.. TurmasDoAluno.Vigentes(user.GroupMemberships, BrasiliaTime.Hoje)
            .Where(m => m.Group != null)
            .Select(m => new UserGroupDto
            {
                Id = m.GroupId, Code = m.Group.Code, Name = m.Group.Name, Shift = TurmasDoAluno.Turno(m.Group)
            })]);

    /// <summary>Fica no backend para a versão aceita ser a mesma em qualquer cliente.</summary>
    [HttpGet("terms")]
    [AllowAnonymous]
    public ActionResult GetTerms() => Ok(new
    {
        titulo = "Termo de Responsabilidade de Acesso",
        versao = TermoVersao,
        itens = new[]
        {
            "A senha de acesso é pessoal e intransferível: não deve ser compartilhada com alunos, colegas ou terceiros, em nenhuma hipótese.",
            "Sou responsável por todas as ações realizadas no sistema com a minha conta, incluindo lançamentos, validações e alterações de dados.",
            "Os dados de alunos acessados aqui são de uso restrito e acadêmico, e não podem ser divulgados fora das atividades de estágio.",
            "Devo encerrar a sessão ao terminar o uso, especialmente em computadores compartilhados.",
            "Comunicarei imediatamente à coordenação qualquer suspeita de uso indevido da minha conta e solicitarei a troca da senha."
        }
    });

    [HttpPost("accept-terms")]
    [Authorize]
    public async Task<IActionResult> AcceptTerms([FromBody] AcceptTermsDto dto)
    {
        if (!dto.Accepted)
            return BadRequest(new { message = "É necessário aceitar o termo para usar o sistema." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

        if (userId == null || !Guid.TryParse(userId, out var id))
            return Unauthorized();

        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.TermsAcceptedAt = BrasiliaTime.Agora;
        user.UpdatedAt = BrasiliaTime.Agora;
        await db.SaveChangesAsync();

        return Ok(new { acceptedAt = user.TermsAcceptedAt, versao = TermoVersao });
    }

    [HttpPost("first-access")]
    [Authorize]
    public async Task<ActionResult<AuthResponseDto>> FirstAccess([FromBody] FirstAccessDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

        if (userId == null || !Guid.TryParse(userId, out var id))
            return Unauthorized();

        var user = await db.Users
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group).ThenInclude(g => g.Schedules)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        if (!user.MustChangePassword && !user.MustSetEmail)
            return BadRequest(new { message = "Primeiro acesso não necessário para este usuário." });

        // E-mail institucional só é exigido do aluno: preceptores costumam ser externos.
        if (user.Role == "aluno" && !EhEmailInstitucional(dto.Email))
            return BadRequest(new { message = "O e-mail deve ser institucional (@cs.udf.edu.br)." });

        var emailTaken = await db.Users.AnyAsync(u => u.Email == dto.Email.ToLower() && u.Id != user.Id);
        if (emailTaken)
            return Conflict(new { message = "E-mail já cadastrado." });

        user.Email = dto.Email.Trim().ToLower();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.MustChangePassword = false;
        user.MustSetEmail = false;
        user.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();

        return Ok(Resposta(user));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email.ToLower());

        // Retorna 200 mesmo se o e-mail não existir para não revelar cadastros
        if (user == null)
            return Ok(new { message = "Se o e-mail estiver cadastrado, você receberá o código em breve." });

        var code = Random.Shared.Next(100000, 999999).ToString();

        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            Email = dto.Email.Trim().ToLower(),
            Code = code,
            ExpiresAt = BrasiliaTime.Agora.AddMinutes(15)
        });
        await db.SaveChangesAsync();

        await emailService.SendResetCodeAsync(user.Email!, code);

        return Ok(new { message = "Se o e-mail estiver cadastrado, você receberá o código em breve." });
    }

    [HttpPost("verify-reset-code")]
    public async Task<IActionResult> VerifyResetCode([FromBody] VerifyResetCodeDto dto)
    {
        var record = await db.PasswordResetCodes.FirstOrDefaultAsync(r =>
            r.Email == dto.Email.ToLower() &&
            r.Code == dto.Code &&
            !r.Used &&
            r.ExpiresAt > BrasiliaTime.Agora);

        if (record == null)
            return BadRequest(new { message = "Código inválido ou expirado." });

        return Ok(new { message = "Código válido." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        var record = await db.PasswordResetCodes.FirstOrDefaultAsync(r =>
            r.Email == dto.Email.ToLower() &&
            r.Code == dto.Code &&
            !r.Used &&
            r.ExpiresAt > BrasiliaTime.Agora);

        if (record == null)
            return BadRequest(new { message = "Código inválido ou expirado." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email.ToLower());
        if (user == null)
            return NotFound(new { message = "Usuário não encontrado." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt = BrasiliaTime.Agora;

        record.Used = true;

        await db.SaveChangesAsync();

        return Ok(new { message = "Senha redefinida com sucesso." });
    }

    private const string DominioInstitucional = "@cs.udf.edu.br";
    private const string TermoVersao = "1.0";

    private static bool EhEmailInstitucional(string email) =>
        email.Trim().EndsWith(DominioInstitucional, StringComparison.OrdinalIgnoreCase);

    private static bool DeveAceitarTermo(Models.ApplicationUser user) =>
        Models.Roles.ExigeTermoResponsabilidade(user.Role) && user.TermsAcceptedAt == null;
}
