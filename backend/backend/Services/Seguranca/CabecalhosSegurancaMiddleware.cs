namespace EstagioCheck.API.Services.Seguranca;

/// <summary>
/// Cabeçalhos de segurança HTTP (OWASP Secure Headers). A API só devolve JSON, então a CSP proíbe
/// tudo; o Swagger UI, que é uma página, fica de fora dela.
/// </summary>
public class CabecalhosSegurancaMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        var h = http.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";

        if (!http.Request.Path.StartsWithSegments("/swagger"))
        {
            h.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            // Resposta autenticada não deve ficar em cache de navegador ou proxy compartilhado.
            h.CacheControl = "no-store";
        }

        return next(http);
    }
}
