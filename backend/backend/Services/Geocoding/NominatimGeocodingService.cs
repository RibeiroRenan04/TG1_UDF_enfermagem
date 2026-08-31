using System.Text.Json;
using System.Text.Json.Serialization;
using EstagioCheck.API.Services.Geocoding;
using Microsoft.Extensions.Options;

namespace EstagioCheck.API.Services.Geocoding;

/// <summary>
/// Geocodificação pelo Nominatim/OpenStreetMap.
///
/// O Nominatim público é um serviço gratuito e de baixa capacidade, mantido por
/// doação. A política de uso exige identificação por User-Agent e no máximo uma
/// requisição por segundo, sem paralelismo. Esta classe garante isso na origem:
/// um semáforo serializa as chamadas do processo inteiro e o intervalo mínimo é
/// respeitado antes de cada requisição, independentemente de quem chamou.
/// </summary>
public class NominatimGeocodingService : IGeocodingService
{
    public const string HttpClientName = "Nominatim";

    /// <summary>
    /// Serializa as requisições ao Nominatim em todo o processo. É estático de
    /// propósito: a política é "uma requisição por vez", não "uma por instância".
    /// </summary>
    private static readonly SemaphoreSlim Portao = new(1, 1);
    private static DateTime _ultimaRequisicaoUtc = DateTime.MinValue;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeocodingOptions _options;
    private readonly ILogger<NominatimGeocodingService> _logger;

    public NominatimGeocodingService(
        IHttpClientFactory httpClientFactory,
        IOptions<GeocodingOptions> options,
        ILogger<NominatimGeocodingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Provedor => OrigemCoordenadasNominatim;

    private const string OrigemCoordenadasNominatim = "NOMINATIM";

    public async Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        var tentativa = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var resposta = await ConsultarAsync(address, cancellationToken);
                return Interpretar(resposta, address);
            }
            catch (GeocodingException ex) when (ex.LimiteExcedido && tentativa < _options.MaxRetries)
            {
                // 429 significa que estamos pressionando demais o serviço. Uma espera
                // longa é o comportamento correto — insistir rápido só piora.
                tentativa++;
                var espera = TimeSpan.FromSeconds(_options.RetryAfterSecondsDefault);
                _logger.LogWarning(
                    "Nominatim respondeu 429. Aguardando {Segundos}s antes da tentativa {Tentativa}/{Max}.",
                    espera.TotalSeconds, tentativa, _options.MaxRetries);
                await Task.Delay(espera, cancellationToken);
            }
            catch (GeocodingException) when (tentativa < _options.MaxRetries)
            {
                // Falha temporária de rede/timeout: uma nova tentativa curta, sem insistência.
                tentativa++;
                _logger.LogWarning(
                    "Falha temporária ao consultar o Nominatim. Tentativa {Tentativa}/{Max}.",
                    tentativa, _options.MaxRetries);
                await Task.Delay(TimeSpan.FromSeconds(2 * tentativa), cancellationToken);
            }
        }
    }

    // ── Requisição, com o intervalo mínimo garantido ──────────────────────────
    private async Task<List<NominatimPlace>> ConsultarAsync(string address, CancellationToken ct)
    {
        await Portao.WaitAsync(ct);
        try
        {
            await AguardarIntervaloMinimoAsync(ct);

            var client = _httpClientFactory.CreateClient(HttpClientName);
            var url = MontarUrl(address);

            HttpResponseMessage resposta;
            try
            {
                resposta = await client.GetAsync(url, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new GeocodingException("Tempo esgotado ao consultar o Nominatim.", inner: ex);
            }
            catch (HttpRequestException ex)
            {
                throw new GeocodingException("Não foi possível alcançar o Nominatim.", inner: ex);
            }
            finally
            {
                // Conta a partir do fim da chamada: é o que o serviço enxerga como ritmo.
                _ultimaRequisicaoUtc = DateTime.UtcNow;
            }

            if (resposta.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                throw new GeocodingException("Nominatim recusou por excesso de requisições.", limiteExcedido: true);

            if (!resposta.IsSuccessStatusCode)
                throw new GeocodingException($"Nominatim respondeu {(int)resposta.StatusCode}.");

            var json = await resposta.Content.ReadAsStringAsync(ct);
            try
            {
                return JsonSerializer.Deserialize<List<NominatimPlace>>(json) ?? [];
            }
            catch (JsonException ex)
            {
                throw new GeocodingException("Resposta do Nominatim em formato inesperado.", inner: ex);
            }
        }
        finally
        {
            Portao.Release();
        }
    }

    private async Task AguardarIntervaloMinimoAsync(CancellationToken ct)
    {
        var intervalo = TimeSpan.FromMilliseconds(_options.RequestDelayMilliseconds);
        var desdeAUltima = DateTime.UtcNow - _ultimaRequisicaoUtc;
        if (desdeAUltima < intervalo)
            await Task.Delay(intervalo - desdeAUltima, ct);
    }

    private string MontarUrl(string address)
    {
        var query = new List<string>
        {
            $"q={Uri.EscapeDataString(address)}",
            "format=jsonv2",
            "addressdetails=1",
            "limit=5"
        };

        if (!string.IsNullOrWhiteSpace(_options.CountryCodes))
            query.Add($"countrycodes={Uri.EscapeDataString(_options.CountryCodes)}");

        return $"/search?{string.Join("&", query)}";
    }

    // ── Escolha do resultado ──────────────────────────────────────────────────
    /// <summary>
    /// O primeiro resultado do Nominatim nem sempre é o certo: uma busca por
    /// "UBS 1" pode devolver a cidade inteira. Preferimos o resultado mais
    /// específico e marcamos como duvidoso o que for genérico demais para servir
    /// de referência a um geofence de poucas centenas de metros.
    /// </summary>
    private GeocodingResult? Interpretar(List<NominatimPlace> lugares, string enderecoConsultado)
    {
        if (lugares.Count == 0)
        {
            _logger.LogInformation("Nominatim não encontrou resultados para o endereço consultado.");
            return null;
        }

        var escolhido = lugares
            .OrderByDescending(p => PesoDaPrecisao(p.Type, p.Category))
            .ThenByDescending(p => p.Importance ?? 0)
            .First();

        if (!double.TryParse(escolhido.Lat, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(escolhido.Lon, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lon))
        {
            _logger.LogWarning("Nominatim devolveu coordenadas ilegíveis; tratando como não encontrado.");
            return null;
        }

        var precisao = escolhido.Type ?? escolhido.Category;
        var peso = PesoDaPrecisao(escolhido.Type, escolhido.Category);

        string? motivo = null;
        if (peso <= PesoGenerico)
            motivo = $"Resultado pouco específico ({precisao}): confira se aponta para a unidade.";
        else if (lugares.Count > 1 && SemNumero(enderecoConsultado))
            motivo = "Endereço sem número e com mais de um resultado possível.";

        return new GeocodingResult(
            lat, lon,
            escolhido.DisplayName,
            precisao,
            Duvidoso: motivo != null,
            MotivoDuvida: motivo);
    }

    // Acima deste peso o resultado aponta para um endereço; abaixo, para uma região.
    private const int PesoGenerico = 2;

    private static int PesoDaPrecisao(string? type, string? category) => type switch
    {
        "house" or "building" or "hospital" or "clinic" or "doctors" or "pharmacy" => 5,
        "house_number" => 5,
        "amenity" or "healthcare" => 4,
        "road" or "residential" or "pedestrian" or "street" => 3,
        "neighbourhood" or "suburb" or "quarter" or "village" => 2,
        "city" or "town" or "municipality" or "county" or "state" or "administrative" => 1,
        _ => category == "amenity" || category == "healthcare" ? 4 : 2
    };

    private static bool SemNumero(string endereco) => !endereco.Any(char.IsDigit);

    // ── Contrato do JSON do Nominatim ─────────────────────────────────────────
    private class NominatimPlace
    {
        [JsonPropertyName("lat")] public string? Lat { get; set; }
        [JsonPropertyName("lon")] public string? Lon { get; set; }
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("class")] public string? Class { get; set; }
        [JsonPropertyName("importance")] public double? Importance { get; set; }
    }
}
