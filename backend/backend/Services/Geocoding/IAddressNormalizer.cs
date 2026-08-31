using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EstagioCheck.API.Services.Geocoding;

/// <summary>Gera a chave usada para consultar e gravar o cache de geocodificação.</summary>
public interface IAddressNormalizer
{
    /// <summary>
    /// Reduz o endereço a uma forma canônica: sem acentos, em minúsculas, sem
    /// pontuação e sem espaços repetidos. "SGAN 906, Brasília - DF" e
    /// "SGAN 906 Brasilia DF" viram a mesma chave.
    /// </summary>
    string Normalizar(string? endereco);

    /// <summary>Padroniza o CEP em "00000-000"; devolve nulo se não tiver 8 dígitos.</summary>
    string? NormalizarCep(string? cep);

    /// <summary>Sigla da UF em maiúsculas; devolve nulo se não tiver 2 letras.</summary>
    string? NormalizarUf(string? uf);
}

public partial class AddressNormalizer : IAddressNormalizer
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex EspacosRepetidos();

    // Mantém letras, dígitos e espaço. A pontuação some porque só varia a escrita
    // do mesmo endereço; o número é preservado, pois distingue unidades na mesma via.
    [GeneratedRegex(@"[^a-z0-9 ]")]
    private static partial Regex ForaDoAlfabeto();

    public string Normalizar(string? endereco)
    {
        if (string.IsNullOrWhiteSpace(endereco)) return string.Empty;

        var semAcento = RemoverAcentos(endereco).ToLowerInvariant();
        var somenteAlfanumerico = ForaDoAlfabeto().Replace(semAcento, " ");
        return EspacosRepetidos().Replace(somenteAlfanumerico, " ").Trim();
    }

    public string? NormalizarCep(string? cep)
    {
        var digitos = new string((cep ?? string.Empty).Where(char.IsDigit).ToArray());
        return digitos.Length == 8 ? $"{digitos[..5]}-{digitos[5..]}" : null;
    }

    public string? NormalizarUf(string? uf)
    {
        var letras = new string((uf ?? string.Empty).Where(char.IsLetter).ToArray());
        return letras.Length == 2 ? letras.ToUpperInvariant() : null;
    }

    private static string RemoverAcentos(string texto)
    {
        var decomposto = texto.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var ch in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
