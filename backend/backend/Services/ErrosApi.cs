using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>Formato único dos erros: <c>{ message, field?, code?, errors? }</c> (<c>errors</c>: campo → mensagens).</summary>
public static class ErrosApi
{
    public static object Corpo(string message, string? field = null, string? code = null) => new
    {
        message,
        field,
        code,
        errors = field == null ? null : new Dictionary<string, string[]> { [field] = [message] }
    };

    /// <summary>400 da validação automática, com mensagens em português e o nome do campo da tela.</summary>
    public static IActionResult RespostaValidacao(ActionContext context)
    {
        var erros = new Dictionary<string, string[]>();

        foreach (var (chave, entrada) in context.ModelState)
        {
            if (entrada.Errors.Count == 0) continue;

            var campo = NomeCampo(chave);
            // A chave do parâmetro ("dto") só diz que o corpo não pôde ser lido; o motivo está nos campos.
            if (string.IsNullOrEmpty(campo) || EhParametroDaAction(context, chave)) continue;

            erros[campo] = [.. entrada.Errors.Select(e => Traduzir(e, campo)).Distinct()];
        }

        var message = erros.Count == 0
            ? "Os dados enviados estão incompletos ou em formato inválido."
            : string.Join(" ", erros.Values.SelectMany(m => m).Distinct().Take(3));

        return new BadRequestObjectResult(new { message, code = "validacao", errors = erros });
    }

    private static bool EhParametroDaAction(ActionContext context, string chave) =>
        context.ActionDescriptor.Parameters.Any(p =>
            string.Equals(p.Name, chave, StringComparison.OrdinalIgnoreCase));

    /// <summary>"$.days[0].mode" / "Days[0].Mode" → "days[0].mode".</summary>
    public static string NomeCampo(string chave)
    {
        var limpa = chave.StartsWith("$.") ? chave[2..] : chave.TrimStart('$');
        return string.Join('.', limpa
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToLowerInvariant(p[0]) + p[1..]));
    }

    private static string Traduzir(ModelError erro, string campo)
    {
        var texto = erro.ErrorMessage ?? string.Empty;

        if (erro.Exception != null || texto.Contains("could not be converted", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("JSON", StringComparison.Ordinal))
            return $"Valor inválido no campo '{campo}'.";

        if (texto.StartsWith("The ", StringComparison.Ordinal) && texto.EndsWith(" field is required.", StringComparison.Ordinal))
            return $"O campo '{campo}' é obrigatório.";

        return string.IsNullOrWhiteSpace(texto) ? $"Valor inválido no campo '{campo}'." : texto;
    }
}

/// <summary>Exceção não tratada vira <c>message</c> legível; violação de restrição do banco sai como 409.</summary>
public class TratadorErrosApi(ILogger<TratadorErrosApi> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, code, message) = ex switch
        {
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "conflito_concorrencia",
                "O registro foi alterado por outra pessoa enquanto você editava. Recarregue a tela e tente novamente."),
            DbUpdateException => (StatusCodes.Status409Conflict, "conflito_dados",
                "Não foi possível salvar: os dados conflitam com um registro existente ou fazem referência a um item que não existe mais."),
            BadHttpRequestException bad => (bad.StatusCode, "requisicao_invalida",
                "A requisição enviada é inválida. Revise os dados e tente novamente."),
            _ => (StatusCodes.Status500InternalServerError, "erro_interno",
                "Erro interno do servidor. Tente novamente em instantes; se o problema continuar, informe o suporte.")
        };

        logger.LogError(ex, "Erro não tratado em {Metodo} {Rota} → {Status}",
            http.Request.Method, http.Request.Path, status);

        http.Response.StatusCode = status;
        // traceId = X-Correlation-Id: o usuário informa ao suporte e o log exato é encontrado.
        await http.Response.WriteAsJsonAsync(new { message, code, traceId = http.TraceIdentifier }, ct);
        return true;
    }
}
