using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Controllers;

/// <summary>
/// Consulta da trilha de auditoria. Só leitura e só para o professor: a trilha cita dados de
/// todos os perfis e não existe rota para alterar ou apagar registros.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Supervisor)]
public class AuditoriaController(AppDbContext db) : ControllerBase
{
    public const int TamanhoMaximoPagina = 200;

    [HttpGet]
    public async Task<ActionResult> Get(
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] Guid? usuarioId,
        [FromQuery] string? acao,
        [FromQuery] string? entidade,
        [FromQuery] string? entidadeId,
        [FromQuery] bool? sucesso,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 50)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, TamanhoMaximoPagina);

        var q = db.AuditLogs.AsNoTracking();
        if (de.HasValue) q = q.Where(l => l.OccurredAt >= de.Value);
        // "até" é inclusivo no dia inteiro quando vem só a data.
        if (ate.HasValue) q = q.Where(l => l.OccurredAt < (ate.Value.TimeOfDay == TimeSpan.Zero ? ate.Value.AddDays(1) : ate.Value));
        if (usuarioId.HasValue) q = q.Where(l => l.UserId == usuarioId.Value);
        if (!string.IsNullOrWhiteSpace(acao)) q = q.Where(l => l.Action == acao.Trim());
        if (!string.IsNullOrWhiteSpace(entidade)) q = q.Where(l => l.Entity == entidade.Trim());
        if (!string.IsNullOrWhiteSpace(entidadeId)) q = q.Where(l => l.EntityId == entidadeId.Trim());
        if (sucesso.HasValue) q = q.Where(l => l.Success == sucesso.Value);

        var total = await q.CountAsync();
        var itens = await q
            .OrderByDescending(l => l.OccurredAt).ThenByDescending(l => l.Id)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync();

        // Nome de quem agiu: a trilha guarda só o id, para não duplicar dado pessoal.
        var ids = itens.Where(l => l.UserId.HasValue).Select(l => l.UserId!.Value).Distinct().ToList();
        var nomes = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return Ok(new
        {
            total,
            pagina,
            tamanho,
            itens = itens.Select(l => new
            {
                l.Id,
                ocorridoEm = l.OccurredAt,
                usuarioId = l.UserId,
                usuario = l.UserId.HasValue ? nomes.GetValueOrDefault(l.UserId.Value) : null,
                papel = l.UserRole,
                acao = l.Action,
                entidade = l.Entity,
                entidadeId = l.EntityId,
                detalhes = l.Details,
                sucesso = l.Success,
                ip = l.IpAddress,
                idCorrelacao = l.CorrelationId
            })
        });
    }
}
