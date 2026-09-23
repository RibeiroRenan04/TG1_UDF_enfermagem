using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CertificatesController(CertificateService certificates, EscopoPreceptorService escopoPreceptor) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<CertificateDto>> GetMine()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);

        var cert = await certificates.ObterAsync(userId);
        return cert == null ? NotFound() : Ok(cert);
    }

    [HttpGet]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<List<CertificateDto>>> GetAll()
    {
        return Ok(await certificates.ListarAsync());
    }

    [HttpGet("{studentId}")]
    [Authorize(Roles = Roles.AcompanhamentoEGestao)]
    public async Task<ActionResult<CertificateDto>> GetByStudent(Guid studentId)
    {
        if (User.IsInRole(Roles.Preceptor))
        {
            var eu = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
            if (!(await escopoPreceptor.CarregarAsync(eu)).Alunos.Contains(studentId)) return Forbid();
        }

        var cert = await certificates.ObterAsync(studentId);
        return cert == null ? NotFound() : Ok(cert);
    }
}
