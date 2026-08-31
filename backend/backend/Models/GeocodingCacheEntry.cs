using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>
/// Resultado de geocodificação guardado por endereço normalizado.
///
/// Existe para não consultar o Nominatim duas vezes pelo mesmo endereço — o serviço
/// público é gratuito e de baixa capacidade, e reimportar a mesma planilha não deve
/// gerar tráfego novo. Também guardamos os "não encontrado", que igualmente não
/// valem uma segunda consulta automática.
/// </summary>
public class GeocodingCacheEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Chave do cache: endereço sem acentos, pontuação nem espaços repetidos.</summary>
    public string EnderecoNormalizado { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>display_name devolvido pelo provedor.</summary>
    public string? EnderecoRetornado { get; set; }

    /// <summary>Precisão informada pelo provedor (type/class do Nominatim).</summary>
    public string? Precisao { get; set; }

    /// <summary>Status da consulta: ver <see cref="StatusGeocodificacao"/>.</summary>
    public string Status { get; set; } = StatusGeocodificacao.Pendente;

    /// <summary>Provedor consultado, para invalidar o cache se ele mudar.</summary>
    public string Provedor { get; set; } = OrigemCoordenadas.Nominatim;

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    /// <summary>Só vale reaproveitar uma entrada que realmente encontrou coordenadas.</summary>
    public bool TemCoordenadas => Latitude.HasValue && Longitude.HasValue;
}
