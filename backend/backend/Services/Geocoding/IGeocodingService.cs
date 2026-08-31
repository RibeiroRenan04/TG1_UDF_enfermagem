namespace EstagioCheck.API.Services.Geocoding;

/// <summary>
/// Converte um endereço em coordenadas.
///
/// A aplicação depende apenas desta interface: trocar o Nominatim por Google,
/// Azure Maps ou HERE é registrar outra implementação no <c>Program.cs</c>, sem
/// tocar em regra de negócio, controller ou tela.
/// </summary>
public interface IGeocodingService
{
    /// <summary>Nome do provedor, gravado junto com a coordenada.</summary>
    string Provedor { get; }

    /// <summary>
    /// Procura as coordenadas de <paramref name="address"/>. Devolve <c>null</c>
    /// quando o endereço não foi encontrado; lança <see cref="GeocodingException"/>
    /// quando a consulta falhou (rede, timeout, limite de uso) — situações que
    /// podem ser tentadas de novo mais tarde.
    /// </summary>
    Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken);
}

/// <summary>Coordenadas encontradas para um endereço.</summary>
public record GeocodingResult(
    double Latitude,
    double Longitude,
    /// <summary>Endereço como o provedor o descreve, para conferência humana.</summary>
    string? EnderecoRetornado,
    /// <summary>Granularidade do resultado, ex: "building", "road", "suburb".</summary>
    string? Precisao,
    /// <summary>
    /// Resultado que casou mal com o endereço pedido — coordenada aproveitável,
    /// porém sujeita a conferência antes de valer para o geofence do check-in.
    /// </summary>
    bool Duvidoso,
    /// <summary>Por que foi marcado como duvidoso; nulo quando o resultado é confiável.</summary>
    string? MotivoDuvida = null);

/// <summary>
/// Falha ao consultar o provedor de geocodificação. Diferente de "não encontrado":
/// aqui vale tentar de novo depois, e a unidade fica com status "erro".
/// </summary>
public class GeocodingException(string message, bool limiteExcedido = false, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>O provedor recusou por excesso de requisições (HTTP 429).</summary>
    public bool LimiteExcedido { get; } = limiteExcedido;
}
