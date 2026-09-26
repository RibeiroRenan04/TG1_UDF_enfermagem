using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using EstagioCheck.API.Services.Auditoria;
using EstagioCheck.API.Services.Privacidade;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

/// <summary>LGPD: aviso de privacidade e direitos do titular.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PrivacidadeController(
    PrivacidadeService privacidade,
    AuditoriaService auditoria,
    Data.AppDbContext db,
    IConfiguration config) : ControllerBase
{
    private const string AreaAuditoria = "Privacidade";

    [HttpGet("aviso")]
    [AllowAnonymous]
    public ActionResult Aviso() => Ok(AvisoPrivacidade.Montar(config));

    /// <summary>O próprio titular baixa seus dados, sem depender de pedido à coordenação.</summary>
    [HttpGet("meus-dados")]
    public async Task<IActionResult> MeusDados()
    {
        var id = UsuarioAtual();
        if (id == null) return Unauthorized();
        return await Exportar(id.Value);
    }

    /// <summary>Para atender um pedido que chegou ao encarregado por outro canal.</summary>
    [HttpGet("usuarios/{id}/dados")]
    [Authorize(Roles = Roles.Supervisor)]
    public Task<IActionResult> DadosDoTitular(Guid id) => Exportar(id);

    [HttpPost("usuarios/{id}/anonimizar")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> Anonimizar(Guid id, [FromBody] AnonimizarDto dto)
    {
        if (!dto.Confirmar)
            return BadRequest(ErrosApi.Corpo("Confirme a anonimização: ela não pode ser desfeita.", "confirmar"));

        var (status, erro) = await privacidade.AnonimizarAsync(id, UsuarioAtual() ?? Guid.Empty);
        if (status == StatusAnonimizacao.NaoEncontrado) return NotFound(ErrosApi.Corpo(erro!));
        if (status == StatusAnonimizacao.Recusada)
            return BadRequest(ErrosApi.Corpo(erro!, code: "anonimizacao_recusada"));

        auditoria.Registrar(AcoesAuditoria.Anonimizacao, AreaAuditoria, id.ToString(), new { motivo = dto.Motivo.Trim() });
        await db.SaveChangesAsync();

        return Ok(new { message = "Dados pessoais anonimizados. O registro acadêmico foi preservado." });
    }

    private async Task<IActionResult> Exportar(Guid id)
    {
        var dados = await privacidade.ExportarAsync(id);
        if (dados == null) return NotFound(ErrosApi.Corpo("Usuário não encontrado."));

        await auditoria.RegistrarAsync(AcoesAuditoria.ExportacaoDados, AreaAuditoria, id.ToString());
        return Ok(dados);
    }

    private Guid? UsuarioAtual() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
            ? id
            : null;
}

public record AnonimizarDto(
    bool Confirmar,
    [Required(ErrorMessage = "Informe o motivo da anonimização."),
     MinLength(5, ErrorMessage = "Descreva o motivo da anonimização."),
     MaxLength(500)] string Motivo
);
