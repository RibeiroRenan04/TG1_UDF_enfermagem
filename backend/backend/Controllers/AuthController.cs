using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using EstagioCheck.API.Services.Auditoria;
using EstagioCheck.API.Services.Privacidade;
using EstagioCheck.API.Services.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(LimitesRequisicao.Autenticacao)]
public class AuthController(
    AppDbContext db,
    TokenService tokenService,
    EmailService emailService,
    AuditoriaService auditoria,
    ProtecaoAcessoService protecao) : ControllerBase
{
    // Sem autocadastro: alunos vêm da importação e os demais perfis, do cadastro do professor.

    private const string AreaAuditoria = "Autenticacao";

    /// <summary>Hash descartável: com ele o login de e-mail inexistente leva o mesmo tempo que o de senha errada.</summary>
    private static readonly string HashFicticio = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        var email = dto.Email.Trim().ToLower();
        var chave = $"login:{email}";

        if (protecao.Bloqueado(chave, out var restante))
        {
            await auditoria.RegistrarAsync(AcoesAuditoria.LoginBloqueado, AreaAuditoria,
                detalhes: new { email = Mascara.Email(email) }, sucesso: false);
            return StatusCode(StatusCodes.Status429TooManyRequests, ErrosApi.Corpo(
                $"Muitas tentativas de acesso sem sucesso. Tente novamente em {ProtecaoAcessoService.Minutos(restante)} minuto(s) ou recupere a senha.",
                code: "login_bloqueado"));
        }

        var user = await db.Users
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group).ThenInclude(g => g.Schedules)
            .FirstOrDefaultAsync(u => u.Email == email);

        var senhaConfere = BCrypt.Net.BCrypt.Verify(dto.Password, user?.PasswordHash ?? HashFicticio);
        if (user == null || !senhaConfere)
        {
            protecao.RegistrarFalha(chave);
            await auditoria.RegistrarAsync(AcoesAuditoria.LoginFalha, AreaAuditoria, user?.Id.ToString(),
                new { email = Mascara.Email(email) }, sucesso: false, usuarioId: user?.Id, papel: user?.Role);
            return Unauthorized(new { message = "Credenciais inválidas." });
        }

        // Aluno concluinte e preceptor desligado perdem o acesso (ISO 27001 A.5.18).
        if (!user.IsActive)
        {
            await auditoria.RegistrarAsync(AcoesAuditoria.LoginContaInativa, AreaAuditoria, user.Id.ToString(),
                sucesso: false, usuarioId: user.Id, papel: user.Role);
            return StatusCode(StatusCodes.Status403Forbidden, ErrosApi.Corpo(
                "Sua conta está inativa. Procure a coordenação do estágio.", code: "conta_inativa"));
        }

        protecao.Limpar(chave);
        await auditoria.RegistrarAsync(AcoesAuditoria.LoginSucesso, AreaAuditoria, user.Id.ToString(),
            usuarioId: user.Id, papel: user.Role);

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
        auditoria.Registrar(AcoesAuditoria.TermoAceito, AreaAuditoria, user.Id.ToString(), new { versao = TermoVersao });
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

        var problemaSenha = PoliticaSenha.Validar(dto.NewPassword, user.Rgm, dto.Email);
        if (problemaSenha != null)
            return BadRequest(ErrosApi.Corpo(problemaSenha, "newPassword", "senha_fraca"));

        var emailTaken = await db.Users.AnyAsync(u => u.Email == dto.Email.ToLower() && u.Id != user.Id);
        if (emailTaken)
            return Conflict(new { message = "E-mail já cadastrado." });

        user.Email = dto.Email.Trim().ToLower();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.MustChangePassword = false;
        user.MustSetEmail = false;
        user.UpdatedAt = BrasiliaTime.Agora;

        auditoria.Registrar(AcoesAuditoria.PrimeiroAcesso, AreaAuditoria, user.Id.ToString());
        await db.SaveChangesAsync();

        return Ok(Resposta(user));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        // Antes de consultar o usuário: a resposta é igual para qualquer e-mail e não revela cadastro.
        if (!emailService.Habilitado)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ErrosApi.Corpo(
                "A recuperação de senha por e-mail está temporariamente desativada. "
                + "Procure a coordenação do estágio para redefinir sua senha.",
                code: "email_desativado"));

        // Mesma resposta em todos os casos, para não revelar quais e-mails têm cadastro.
        var resposta = new { message = "Se o e-mail estiver cadastrado, você receberá o código em breve." };

        var email = dto.Email.Trim().ToLower();
        var chave = $"recuperacao:{email}";

        // Limita os envios por conta: sem isso, dá para inundar a caixa de alguém com códigos.
        if (protecao.Bloqueado(chave, out _)) return Ok(resposta);
        protecao.RegistrarFalha(chave);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null || !user.IsActive) return Ok(resposta);

        // Só o código mais recente vale: os anteriores deixam de ser aceitos.
        await foreach (var anterior in db.PasswordResetCodes
            .Where(r => r.Email == email && !r.Used).AsAsyncEnumerable())
            anterior.Used = true;

        // Gerador criptográfico: Random não é imprevisível o bastante para um código de acesso.
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            Email = email,
            Code = code,
            ExpiresAt = BrasiliaTime.Agora.AddMinutes(15)
        });
        auditoria.Registrar(AcoesAuditoria.RecuperacaoSolicitada, AreaAuditoria, user.Id.ToString(),
            usuarioId: user.Id, papel: user.Role);
        await db.SaveChangesAsync();

        await emailService.SendResetCodeAsync(user.Email!, code);

        return Ok(resposta);
    }

    [HttpPost("verify-reset-code")]
    public async Task<IActionResult> VerifyResetCode([FromBody] VerifyResetCodeDto dto)
    {
        var (record, erro) = await ConferirCodigoAsync(dto.Email, dto.Code);
        if (record == null) return erro!;

        return Ok(new { message = "Código válido." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        var (record, erro) = await ConferirCodigoAsync(dto.Email, dto.Code);
        if (record == null) return erro!;

        var email = dto.Email.Trim().ToLower();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
            return NotFound(new { message = "Usuário não encontrado." });

        var problemaSenha = PoliticaSenha.Validar(dto.NewPassword, user.Rgm, user.Email);
        if (problemaSenha != null)
            return BadRequest(ErrosApi.Corpo(problemaSenha, "newPassword", "senha_fraca"));

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt = BrasiliaTime.Agora;

        record.Used = true;

        auditoria.Registrar(AcoesAuditoria.SenhaRedefinida, AreaAuditoria, user.Id.ToString(),
            usuarioId: user.Id, papel: user.Role);
        await db.SaveChangesAsync();

        // Senha nova destrava a conta: quem esqueceu a senha normalmente acabou de errar várias vezes.
        protecao.Limpar($"login:{email}");
        protecao.Limpar($"codigo:{email}");

        return Ok(new { message = "Senha redefinida com sucesso." });
    }

    /// <summary>
    /// Código de 6 dígitos tem só 900 mil combinações: sem limite de tentativas, cai por força
    /// bruta dentro dos 15 minutos de validade. Depois de 5 erros a conta fica bloqueada.
    /// </summary>
    private async Task<(PasswordResetCode? Registro, IActionResult? Erro)> ConferirCodigoAsync(string emailInformado, string code)
    {
        var email = emailInformado.Trim().ToLower();
        var chave = $"codigo:{email}";

        if (protecao.Bloqueado(chave, out var restante))
            return (null, StatusCode(StatusCodes.Status429TooManyRequests, ErrosApi.Corpo(
                $"Muitas tentativas com código inválido. Aguarde {ProtecaoAcessoService.Minutos(restante)} minuto(s) e solicite um novo código.",
                code: "codigo_bloqueado")));

        var record = await db.PasswordResetCodes.FirstOrDefaultAsync(r =>
            r.Email == email &&
            r.Code == code &&
            !r.Used &&
            r.ExpiresAt > BrasiliaTime.Agora);

        if (record != null) return (record, null);

        protecao.RegistrarFalha(chave);
        await auditoria.RegistrarAsync(AcoesAuditoria.RecuperacaoCodigoInvalido, AreaAuditoria,
            detalhes: new { email = Mascara.Email(email) }, sucesso: false);
        return (null, BadRequest(new { message = "Código inválido ou expirado." }));
    }

    private const string DominioInstitucional = "@cs.udf.edu.br";
    private const string TermoVersao = "1.0";

    private static bool EhEmailInstitucional(string email) =>
        email.Trim().EndsWith(DominioInstitucional, StringComparison.OrdinalIgnoreCase);

    private static bool DeveAceitarTermo(Models.ApplicationUser user) =>
        Models.Roles.ExigeTermoResponsabilidade(user.Role) && user.TermsAcceptedAt == null;
}
