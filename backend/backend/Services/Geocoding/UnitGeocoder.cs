using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services.Geocoding;

/// <summary>
/// Regras em volta do provedor: cache, respeito às coordenadas manuais e gravação do resultado
/// (inclusive "não encontrado"). O resto da aplicação nunca chama o <see cref="IGeocodingService"/> direto.
/// </summary>
public class UnitGeocoder(
    AppDbContext db,
    IGeocodingService geocoder,
    IAddressNormalizer normalizer,
    ILogger<UnitGeocoder> logger)
{
    public record Resultado(
        string Status,
        bool Sucesso,
        double? Latitude,
        double? Longitude,
        string? EnderecoEncontrado,
        string? Precisao,
        string? Mensagem,
        bool VeioDoCache);

    /// <summary>
    /// <paramref name="forcar"/> ignora cache e coordenadas atuais; ainda assim, as de origem MANUAL
    /// só são sobrescritas com <paramref name="sobrescreverManual"/>.
    /// </summary>
    public async Task<Resultado> GeocodificarAsync(
        Location unidade,
        bool forcar = false,
        bool sobrescreverManual = false,
        CancellationToken ct = default)
    {
        if (unidade.CoordenadaManual && !sobrescreverManual)
        {
            return new Resultado(StatusGeocodificacao.Sucesso, true,
                unidade.Latitude, unidade.Longitude, unidade.EnderecoGeocodificado,
                unidade.PrecisaoLocalizacao,
                "Coordenadas definidas manualmente foram preservadas.", VeioDoCache: false);
        }

        if (!forcar && unidade.TemCoordenadas && unidade.StatusGeocodificacao == StatusGeocodificacao.Sucesso)
        {
            return new Resultado(StatusGeocodificacao.Sucesso, true,
                unidade.Latitude, unidade.Longitude, unidade.EnderecoGeocodificado,
                unidade.PrecisaoLocalizacao,
                "A unidade já possui coordenadas.", VeioDoCache: false);
        }

        var consulta = MontarConsulta(unidade);
        if (string.IsNullOrWhiteSpace(consulta))
        {
            return Aplicar(unidade, StatusGeocodificacao.NaoEncontrado, null,
                "Endereço insuficiente para localizar a unidade.", false);
        }

        // Cache por endereço, não pela consulta inteira: unidades no mesmo endereço compartilham coordenada.
        var chave = normalizer.Normalizar(unidade.EnderecoCompleto);

        // Só com o nome a chave seria fraca demais para compartilhar.
        var podeUsarCache = !string.IsNullOrWhiteSpace(chave);

        // 1) Cache: evita repetir a mesma consulta ao provedor.
        if (!forcar && podeUsarCache)
        {
            var emCache = await db.GeocodingCache
                .FirstOrDefaultAsync(c => c.EnderecoNormalizado == chave, ct);

            if (emCache != null)
            {
                logger.LogInformation("Geocodificação de {Unidade} atendida pelo cache.", unidade.Name);

                if (emCache.TemCoordenadas)
                {
                    unidade.Latitude = emCache.Latitude!.Value;
                    unidade.Longitude = emCache.Longitude!.Value;
                    unidade.EnderecoGeocodificado = emCache.EnderecoRetornado;
                    unidade.PrecisaoLocalizacao = emCache.Precisao;
                    unidade.OrigemCoordenadas = geocoder.Provedor;
                    unidade.GeocodificadoEm = BrasiliaTime.Agora;
                    unidade.StatusGeocodificacao = emCache.Status;
                    unidade.UpdatedAt = BrasiliaTime.Agora;

                    return new Resultado(emCache.Status, true, emCache.Latitude, emCache.Longitude,
                        emCache.EnderecoRetornado, emCache.Precisao, null, VeioDoCache: true);
                }

                unidade.StatusGeocodificacao = emCache.Status;
                unidade.UpdatedAt = BrasiliaTime.Agora;
                return new Resultado(emCache.Status, false, null, null, null, null,
                    "Endereço já consultado anteriormente sem resultado.", VeioDoCache: true);
            }
        }

        GeocodingResult? resultado;
        try
        {
            resultado = await geocoder.GeocodeAsync(consulta, ct);
        }
        catch (GeocodingException ex)
        {
            // Falha de comunicação não é "endereço inexistente": não vai para o cache.
            logger.LogWarning(ex, "Falha ao geocodificar a unidade {Unidade}.", unidade.Name);
            return Aplicar(unidade, StatusGeocodificacao.Erro, null,
                ex.LimiteExcedido
                    ? "Limite de uso do serviço de geocodificação atingido. Tente novamente mais tarde."
                    : "Não foi possível consultar o serviço de geocodificação. Tente novamente mais tarde.",
                false);
        }

        // 3) Grava no cache (inclusive o "não encontrado") e aplica na unidade.
        if (resultado == null)
        {
            if (podeUsarCache)
                await SalvarCacheAsync(chave, null, StatusGeocodificacao.NaoEncontrado, ct);
            return Aplicar(unidade, StatusGeocodificacao.NaoEncontrado, null,
                "Não foi possível encontrar uma localização confiável.", false);
        }

        var status = resultado.Duvidoso
            ? StatusGeocodificacao.RevisaoManual
            : StatusGeocodificacao.Sucesso;

        if (podeUsarCache)
            await SalvarCacheAsync(chave, resultado, status, ct);
        return Aplicar(unidade, status, resultado, resultado.MotivoDuvida, true);
    }

    /// <summary>Nome + endereço + cidade + UF: só o nome cai no centro da cidade, só a via não distingue unidades.</summary>
    public static string MontarConsulta(Location unidade)
    {
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(unidade.Name)) partes.Add(unidade.Name.Trim());

        var endereco = unidade.EnderecoCompleto;
        if (!string.IsNullOrWhiteSpace(endereco)) partes.Add(endereco);

        if (partes.Count == 0) return string.Empty;

        partes.Add("Brasil");
        return string.Join(", ", partes);
    }

    private Resultado Aplicar(Location unidade, string status, GeocodingResult? r, string? mensagem, bool sucesso)
    {
        if (r != null)
        {
            unidade.Latitude = r.Latitude;
            unidade.Longitude = r.Longitude;
            unidade.EnderecoGeocodificado = r.EnderecoRetornado;
            unidade.PrecisaoLocalizacao = r.Precisao;
            unidade.OrigemCoordenadas = geocoder.Provedor;
            unidade.GeocodificadoEm = BrasiliaTime.Agora;
        }

        unidade.StatusGeocodificacao = status;
        unidade.UpdatedAt = BrasiliaTime.Agora;

        return new Resultado(status, sucesso, r?.Latitude, r?.Longitude,
            r?.EnderecoRetornado, r?.Precisao, mensagem, VeioDoCache: false);
    }

    private async Task SalvarCacheAsync(string chave, GeocodingResult? r, string status, CancellationToken ct)
    {
        var existente = await db.GeocodingCache.FirstOrDefaultAsync(c => c.EnderecoNormalizado == chave, ct);
        if (existente == null)
        {
            db.GeocodingCache.Add(new GeocodingCacheEntry
            {
                EnderecoNormalizado = chave,
                Latitude = r?.Latitude,
                Longitude = r?.Longitude,
                EnderecoRetornado = r?.EnderecoRetornado,
                Precisao = r?.Precisao,
                Status = status,
                Provedor = geocoder.Provedor
            });
            return;
        }

        existente.Latitude = r?.Latitude;
        existente.Longitude = r?.Longitude;
        existente.EnderecoRetornado = r?.EnderecoRetornado;
        existente.Precisao = r?.Precisao;
        existente.Status = status;
        existente.Provedor = geocoder.Provedor;
        existente.UpdatedAt = BrasiliaTime.Agora;
    }
}
