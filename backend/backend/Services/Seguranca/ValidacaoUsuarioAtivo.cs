using EstagioCheck.API.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EstagioCheck.API.Services.Seguranca;

/// <summary>
/// O JWT vale por horas; sem esta checagem, quem é desativado continuaria usando o sistema até o
/// token expirar. O status fica em cache por um minuto para não consultar o banco a cada chamada.
/// </summary>
public static class ValidacaoUsuarioAtivo
{
    public static readonly TimeSpan Cache = TimeSpan.FromMinutes(1);

    public static async Task AoValidarToken(TokenValidatedContext ctx)
    {
        var sub = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(sub, out var id))
        {
            ctx.Fail("Token sem usuário.");
            return;
        }

        var servicos = ctx.HttpContext.RequestServices;
        var ativo = await EstaAtivoAsync(
            id, servicos.GetRequiredService<IMemoryCache>(), servicos.GetRequiredService<AppDbContext>());

        if (!ativo) ctx.Fail("Conta inativa ou removida.");
    }

    public static async Task<bool> EstaAtivoAsync(Guid id, IMemoryCache cache, AppDbContext db) =>
        await cache.GetOrCreateAsync($"usuario-ativo:{id}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = Cache;
            return await db.Users.AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => (bool?)u.IsActive)
                .FirstOrDefaultAsync() ?? false;
        });
}
