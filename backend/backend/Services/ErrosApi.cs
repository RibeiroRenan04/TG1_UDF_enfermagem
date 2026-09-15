using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>
/// Formato único das respostas de erro da API:
/// <c>{ message, field?, code?, errors? }</c>.
///
/// A tela mostra <c>message</c> no aviso e usa <c>errors</c> (campo → mensagens)
/// para destacar os campos com problema. Antes, a validação automática do ASP.NET
/// respondia no formato ProblemDetails (sem <c>message</c>) e as falhas não
/// tratadas voltavam sem corpo — nos dois casos a tela caía no genérico
/// "Erro ao salvar", sem dizer o que corrigir.
/// </summary>
public static class ErrosApi
{
    /// <summary>Corpo de erro de negócio, opcionalmente amarrado a um campo do formulário.</summary>
    public static object Corpo(string message, string? field = null, string? code = null) => new
    {
        message,
        field,
        code,
        errors = field == null ? null : new Dictionary<string, string[]> { [field] = [message] }
    };

    /// <summary>
    /// Resposta 400 da validação automática (<c>[Required]</c>, <c>[Range]</c>, JSON
    /// malformado). As mensagens do framework vêm em inglês e com o caminho do JSON
    /// ("$.startDate"); aqui viram texto em português e o nome do campo da tela.
    /// </summary>
    public static IActionResult RespostaValidacao(ActionContext context)
    {
        var erros = new Dictionary<string, string[]>();

        foreach (var (chave, entrada) in context.ModelState)
        {
            if (entrada.Errors.Count == 0) continue;

            var campo = NomeCampo(chave);
            // A chave com o nome do parâmetro ("dto") só diz que o corpo inteiro não
            // pôde ser lido — o motivo real está nas chaves dos campos.
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

/// <summary>
/// Última barreira: exceção não tratada vira resposta com <c>message</c> legível,
/// em vez de um 500 sem corpo. Falha de gravação por restrição do banco (registro
/// duplicado, referência a item removido) sai como 409, que é o que ela é.
/// </summary>
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
        await http.Response.WriteAsJsonAsync(new { message, code }, ct);
        return true;
    }
}
