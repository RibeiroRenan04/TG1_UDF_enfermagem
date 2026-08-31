namespace EstagioCheck.API.Services.Geocoding;

/// <summary>Seção "Geocoding" do appsettings. Nada aqui fica no código.</summary>
public class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    public string Provider { get; set; } = "Nominatim";
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    /// <summary>
    /// Identificação da aplicação exigida pela política de uso do Nominatim. Precisa
    /// conter um contato real: sem isso o serviço público pode bloquear as chamadas.
    /// </summary>
    public string UserAgent { get; set; } = "EstagioCheck/1.0";

    /// <summary>
    /// Intervalo mínimo entre duas requisições. A política do Nominatim público é de
    /// no máximo 1 req/s; 1100 ms dá folga para variação de relógio e rede.
    /// </summary>
    public int RequestDelayMilliseconds { get; set; } = 1100;

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Tentativas extras após uma falha temporária. Deliberadamente baixo.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Espera após um HTTP 429, quando o provedor não manda "Retry-After".</summary>
    public int RetryAfterSecondsDefault { get; set; } = 60;

    /// <summary>Restringe a busca a um país (código ISO). Vazio = sem restrição.</summary>
    public string CountryCodes { get; set; } = "br";

    /// <summary>Idioma preferido nos endereços devolvidos.</summary>
    public string AcceptLanguage { get; set; } = "pt-BR";
}
