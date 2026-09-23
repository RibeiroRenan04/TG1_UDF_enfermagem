using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace EstagioCheck.API.Services;

/// <summary>
/// Unidades do DF pela API pública do CNES. Ela não filtra por nome e devolve no máximo 20 por
/// página: paginamos a lista completa, guardamos em cache e filtramos aqui.
/// </summary>
public class BuscaSaudeService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<BuscaSaudeService> logger)
{
    private const string BaseUrl = "https://apidadosabertos.saude.gov.br/cnes/estabelecimentos";
    private const int CodigoUfDf = 53;
    // Tipo 2 = "CENTRO DE SAUDE/UNIDADE BASICA" (UBS) — equivalente ao Busca Saúde UBS da SES-DF.
    public const int TipoUbs = 2;
    // A API ignora limites acima de 20 por página.
    private const int PageSize = 40;
    // 70 páginas = 2800 unidades.
    private const int MaxPages = 70;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    /// <summary>Filtro por nome ou endereço, sem diferenciar acentos/maiúsculas.</summary>
    public async Task<List<BuscaSaudeEstabelecimentoDto>> BuscarAsync(
        string? termo = null,
        int tipoUnidade = TipoUbs,
        int max = 50)
    {
        var todos = await ObterUnidadesDfAsync(tipoUnidade);

        IEnumerable<BuscaSaudeEstabelecimentoDto> resultado = todos;
        if (!string.IsNullOrWhiteSpace(termo))
        {
            var alvo = Normalizar(termo);
            resultado = todos.Where(e =>
                Normalizar(e.Nome).Contains(alvo) ||
                Normalizar(e.Endereco).Contains(alvo));
        }

        return resultado.Take(max).ToList();
    }

    private async Task<List<BuscaSaudeEstabelecimentoDto>> ObterUnidadesDfAsync(int tipoUnidade)
    {
        var cacheKey = $"cnes_df_{tipoUnidade}";
        if (cache.TryGetValue(cacheKey, out List<BuscaSaudeEstabelecimentoDto>? cached) && cached is not null)
            return cached;

        var client = httpClientFactory.CreateClient("BuscaSaude");
        var unidades = new List<BuscaSaudeEstabelecimentoDto>();

        try
        {
            for (var pagina = 0; pagina < MaxPages; pagina++)
            {
                var offset = pagina * PageSize;
                var url = $"{BaseUrl}?codigo_uf={CodigoUfDf}&codigo_tipo_unidade={tipoUnidade}&limit={PageSize}&offset={offset}";

                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<CnesRoot>(json, JsonOptions);
                var estabelecimentos = root?.Estabelecimentos ?? [];

                unidades.AddRange(estabelecimentos
                    .Where(e => e.Latitude.HasValue && e.Longitude.HasValue)
                    .Select(Map));

                if (estabelecimentos.Count < PageSize) break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao consultar API CNES (Busca Saúde DF).");
            if (unidades.Count == 0) throw;
        }

        var ordenadas = unidades
            .OrderBy(e => e.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList();

        cache.Set(cacheKey, ordenadas, CacheTtl);
        return ordenadas;
    }

    private static BuscaSaudeEstabelecimentoDto Map(CnesEstabelecimento e) => new(
        CodigoCnes: Models.Cnes.Formatar(e.CodigoCnes),
        Nome: !string.IsNullOrEmpty(e.NomeFantasia) ? e.NomeFantasia : e.NomeRazaoSocial,
        Endereco: FormatarEndereco(e),
        Latitude: e.Latitude!.Value,
        Longitude: e.Longitude!.Value,
        Telefone: e.Telefone,
        TurnoAtendimento: e.DescricaoTurnoAtendimento
    );

    private static string FormatarEndereco(CnesEstabelecimento e)
    {
        var partes = new List<string>();
        if (!string.IsNullOrEmpty(e.EnderecoLogradouro)) partes.Add(e.EnderecoLogradouro);
        if (!string.IsNullOrEmpty(e.NumeroEstabelecimento)) partes.Add(e.NumeroEstabelecimento);
        if (!string.IsNullOrEmpty(e.BairroEstabelecimento)) partes.Add(e.BairroEstabelecimento);
        if (!string.IsNullOrEmpty(e.CodigoCep)) partes.Add(e.CodigoCep);
        return string.Join(", ", partes);
    }

    private static string Normalizar(string texto)
    {
        var decomposto = texto.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var ch in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class CnesRoot
    {
        [JsonPropertyName("estabelecimentos")]
        public List<CnesEstabelecimento> Estabelecimentos { get; set; } = [];
    }

    private sealed class CnesEstabelecimento
    {
        [JsonPropertyName("codigo_cnes")]
        public long CodigoCnes { get; set; }

        [JsonPropertyName("nome_razao_social")]
        public string NomeRazaoSocial { get; set; } = string.Empty;

        [JsonPropertyName("nome_fantasia")]
        public string? NomeFantasia { get; set; }

        [JsonPropertyName("endereco_estabelecimento")]
        public string? EnderecoLogradouro { get; set; }

        [JsonPropertyName("numero_estabelecimento")]
        public string? NumeroEstabelecimento { get; set; }

        [JsonPropertyName("bairro_estabelecimento")]
        public string? BairroEstabelecimento { get; set; }

        [JsonPropertyName("codigo_cep_estabelecimento")]
        public string? CodigoCep { get; set; }

        [JsonPropertyName("numero_telefone_estabelecimento")]
        public string? Telefone { get; set; }

        [JsonPropertyName("descricao_turno_atendimento")]
        public string? DescricaoTurnoAtendimento { get; set; }

        [JsonPropertyName("latitude_estabelecimento_decimo_grau")]
        public double? Latitude { get; set; }

        [JsonPropertyName("longitude_estabelecimento_decimo_grau")]
        public double? Longitude { get; set; }
    }
}

public record BuscaSaudeEstabelecimentoDto(
    string CodigoCnes,
    string Nome,
    string Endereco,
    double Latitude,
    double Longitude,
    string? Telefone,
    string? TurnoAtendimento
);
