using System.Threading.RateLimiting;

namespace EstagioCheck.API.Services.Seguranca;

/// <summary>
/// Limite de requisições por IP nas rotas de autenticação, contra varredura de senhas em muitas
/// contas. Generoso de propósito: vários alunos entram pelo mesmo IP do campus na troca de turno.
/// A proteção de cada conta fica com o <see cref="ProtecaoAcessoService"/>.
/// </summary>
public static class LimitesRequisicao
{
    public const string Autenticacao = "autenticacao";

    public static IServiceCollection AddLimitesRequisicao(this IServiceCollection services, IConfiguration config)
    {
        var porMinuto = config.GetValue("Seguranca:AutenticacaoPorMinutoPorIp", 60);

        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)espera.TotalSeconds).ToString();

                await ctx.HttpContext.Response.WriteAsJsonAsync(ErrosApi.Corpo(
                    "Muitas requisições em pouco tempo. Aguarde um minuto e tente novamente.",
                    code: "limite_requisicoes"), ct);
            };

            o.AddPolicy(Autenticacao, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = porMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
    }
}
