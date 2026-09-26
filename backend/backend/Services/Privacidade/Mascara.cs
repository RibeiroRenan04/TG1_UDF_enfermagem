namespace EstagioCheck.API.Services.Privacidade;

/// <summary>Minimização em logs e auditoria (LGPD art. 6º, III): identifica sem expor o dado inteiro.</summary>
public static class Mascara
{
    /// <summary>"joao.santos@cs.udf.edu.br" → "jo***@cs.udf.edu.br".</summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "***";
        var arroba = email.IndexOf('@');
        if (arroba <= 0) return "***";
        return $"{email[..Math.Min(2, arroba)]}***{email[arroba..]}";
    }
}
