using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EstagioCheck.API.Services.Geocoding;

public interface IAddressNormalizer
{
    /// <summary>"SGAN 906, Brasília - DF" e "SGAN 906 Brasilia DF" viram a mesma chave.</summary>
    string Normalizar(string? endereco);

    /// <summary>"00000-000"; nulo se não tiver 8 dígitos.</summary>
    string? NormalizarCep(string? cep);

    string? NormalizarUf(string? uf);
}

public partial class AddressNormalizer : IAddressNormalizer
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex EspacosRepetidos();

    // O número é preservado: distingue unidades na mesma via.
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
