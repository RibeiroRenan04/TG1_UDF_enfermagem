using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>Evita consultar o Nominatim duas vezes pelo mesmo endereço (inclusive os "não encontrado").</summary>
public class GeocodingCacheEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Sem acentos, pontuação nem espaços repetidos.</summary>
    public string EnderecoNormalizado { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? EnderecoRetornado { get; set; }

    public string? Precisao { get; set; }

    public string Status { get; set; } = StatusGeocodificacao.Pendente;

    /// <summary>Provedor consultado, para invalidar o cache se ele mudar.</summary>
    public string Provedor { get; set; } = OrigemCoordenadas.Nominatim;

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    public bool TemCoordenadas => Latitude.HasValue && Longitude.HasValue;
}
