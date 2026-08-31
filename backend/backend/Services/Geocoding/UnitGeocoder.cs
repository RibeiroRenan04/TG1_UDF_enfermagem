using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services.Geocoding;

/// <summary>
/// Geocodifica uma unidade de saúde aplicando as regras de negócio em volta do
/// provedor: consulta o cache antes, respeita coordenadas definidas à mão e grava
/// o resultado (inclusive os "não encontrado", que também não valem nova consulta).
///
/// Todo o resto da aplicação passa por aqui, nunca direto pelo
/// <see cref="IGeocodingService"/> — é o que mantém a política de uso do provedor
/// em um só lugar.
/// </summary>
public class UnitGeocoder(
    AppDbContext db,
    IGeocodingService geocoder,
    IAddressNormalizer normalizer,
    ILogger<UnitGeocoder> logger)
{
    /// <summary>Resultado de uma tentativa de geocodificar uma unidade.</summary>
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
    /// Geocodifica a unidade e grava o resultado nela.
    ///
    /// <paramref name="forcar"/> ignora o cache e as coordenadas já existentes —
    /// é a ação "geocodificar novamente" do administrador. Mesmo assim, coordenadas
    /// de origem MANUAL só são sobrescritas com <paramref name="sobrescreverManual"/>,
    /// para que uma reimportação nunca desfaça uma correção feita à mão.
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

        // A chave do cache é o ENDEREÇO normalizado, não a consulta inteira: o nome
        // da unidade entra na consulta para ajudar o provedor a acertar o ponto, mas
        // duas unidades no mesmo endereço (anexos de um complexo, por exemplo) devem
        // compartilhar a mesma coordenada em vez de gerar duas consultas ao serviço
        // público. À precisão de um geofence de centenas de metros, o mesmo endereço
        // é o mesmo lugar.
        var chave = normalizer.Normalizar(unidade.EnderecoCompleto);

        // Sem endereço só resta o nome, que é fraco demais para servir de chave
        // compartilhada: nesse caso consultamos sem passar pelo cache.
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

        // 2) Provedor.
        GeocodingResult? resultado;
        try
        {
            resultado = await geocoder.GeocodeAsync(consulta, ct);
        }
        catch (GeocodingException ex)
        {
            // Falha de comunicação não é "endereço inexistente": não vai para o cache,
            // a unidade fica com status "erro" e o administrador tenta de novo depois.
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

    /// <summary>
    /// Consulta usada no provedor. Leva nome, endereço, cidade e UF: só o nome
    /// costuma cair no centro da cidade, e só a via não distingue duas unidades
    /// no mesmo logradouro.
    /// </summary>
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
