using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace EstagioCheck.API.Services.Auditoria;

/// <summary>Quem está por trás da requisição atual, para a trilha de auditoria.</summary>
public record ContextoRequisicao(Guid? UsuarioId, string? Papel, string? Ip, string? UserAgent, string? IdCorrelacao)
{
    public static readonly ContextoRequisicao Sistema = new(null, "sistema", null, null, null);

    public static ContextoRequisicao De(HttpContext? http)
    {
        if (http == null) return Sistema;

        var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return new ContextoRequisicao(
            Guid.TryParse(sub, out var id) ? id : null,
            http.User.FindFirstValue(ClaimTypes.Role),
            http.Connection.RemoteIpAddress?.ToString(),
            Cortar(http.Request.Headers.UserAgent.ToString(), 300),
            http.TraceIdentifier);
    }

    private static string? Cortar(string? texto, int max) =>
        string.IsNullOrEmpty(texto) ? null : texto.Length <= max ? texto : texto[..max];
}

/// <summary>
/// Dá a cada requisição um identificador (X-Correlation-Id) que volta na resposta, entra no escopo
/// de log e na auditoria: com ele o suporte acha o log exato de um erro relatado pelo usuário.
/// </summary>
public partial class CorrelacaoMiddleware(RequestDelegate next, ILogger<CorrelacaoMiddleware> logger)
{
    public const string Cabecalho = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext http)
    {
        var recebido = http.Request.Headers[Cabecalho].ToString();
        // Só aceita o id do cliente se for curto e sem caracteres de controle (evita injeção em log).
        http.TraceIdentifier = !string.IsNullOrEmpty(recebido) && IdValido().IsMatch(recebido)
            ? recebido
            : Guid.NewGuid().ToString("N");

        http.Response.OnStarting(() =>
        {
            http.Response.Headers[Cabecalho] = http.TraceIdentifier;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = http.TraceIdentifier }))
            await next(http);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$")]
    private static partial Regex IdValido();
}
