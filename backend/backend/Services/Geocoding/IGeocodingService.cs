namespace EstagioCheck.API.Services.Geocoding;

/// <summary>Trocar o Nominatim por outro provedor é registrar outra implementação no <c>Program.cs</c>.</summary>
public interface IGeocodingService
{
    /// <summary>Nome do provedor, gravado junto com a coordenada.</summary>
    string Provedor { get; }

    /// <summary><c>null</c> quando não encontrado; <see cref="GeocodingException"/> quando a consulta falhou.</summary>
    Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken);
}

public record GeocodingResult(
    double Latitude,
    double Longitude,
    string? EnderecoRetornado,
    /// <summary>Granularidade do resultado, ex: "building", "road", "suburb".</summary>
    string? Precisao,
    /// <summary>Casou mal com o endereço pedido: precisa de conferência antes de valer para o check-in.</summary>
    bool Duvidoso,
    string? MotivoDuvida = null);

/// <summary>Falha na consulta (diferente de "não encontrado"): vale tentar de novo depois.</summary>
public class GeocodingException(string message, bool limiteExcedido = false, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>O provedor recusou por excesso de requisições (HTTP 429).</summary>
    public bool LimiteExcedido { get; } = limiteExcedido;
}
