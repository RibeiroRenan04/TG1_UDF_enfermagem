namespace EstagioCheck.API.Services.Seguranca;

/// <summary>
/// Regras da senha escolhida pelo usuário (ISO 27001 A.5.17). A senha inicial do aluno é o RGM,
/// então a nova não pode repeti-lo — senão a troca obrigatória do primeiro acesso não protege nada.
/// </summary>
public static class PoliticaSenha
{
    public const int TamanhoMinimo = 8;

    public const string Descricao =
        "A senha deve ter pelo menos 8 caracteres, com letras e números.";

    /// <summary>Mensagem do problema, ou <c>null</c> se a senha atende à política.</summary>
    public static string? Validar(string? senha, string? rgm = null, string? email = null)
    {
        if (string.IsNullOrEmpty(senha) || senha.Length < TamanhoMinimo
            || !senha.Any(char.IsLetter) || !senha.Any(char.IsDigit))
            return Descricao;

        // Trechos curtos dariam falso positivo ("ana" em "banana12"), por isso o mínimo de 4.
        if (rgm is { Length: >= 4 } && senha.Contains(rgm, StringComparison.Ordinal))
            return "A senha não pode conter o seu RGM.";

        var usuarioEmail = email?.Split('@')[0];
        if (usuarioEmail is { Length: >= 4 }
            && senha.Contains(usuarioEmail, StringComparison.OrdinalIgnoreCase))
            return "A senha não pode conter o seu login.";

        return null;
    }
}
