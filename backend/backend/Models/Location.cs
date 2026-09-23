using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>Unidade de saúde (tabela "Locais"). As coordenadas daqui são as que validam o check-in.</summary>
public class Location
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int RadiusMeters { get; set; } = 100;
    public bool IsInstitution { get; set; }

    /// <summary>Horário de início do turno, ex: "07:00"</summary>
    public string ShiftStart { get; set; } = "07:00";

    /// <summary>Horário de fim do turno, ex: "13:00"</summary>
    public string ShiftEnd { get; set; } = "13:00";

    /// <summary>Código CNES quando importado via Busca Saúde DF.</summary>
    public string? CodigoCnes { get; set; }

    /// <summary>Tipo da unidade: "UBS", "Hospital", "UPA", "Instituição de ensino"…</summary>
    public string? Tipo { get; set; }

    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }

    public string? Uf { get; set; }

    public string? Cep { get; set; }
    public string? Telefone { get; set; }

    /// <summary>Unidade inativa não recebe novas alocações nem aparece nas listas padrão.</summary>
    public bool Ativo { get; set; } = true;

    public string? OrigemCoordenadas { get; set; }

    public string? StatusGeocodificacao { get; set; }

    /// <summary>Endereço que o provedor devolveu (display_name), para conferência.</summary>
    public string? EnderecoGeocodificado { get; set; }

    /// <summary>Precisão informada pelo provedor, ex: "building", "road", "suburb".</summary>
    public string? PrecisaoLocalizacao { get; set; }

    public DateTime? GeocodificadoEm { get; set; }

    /// <summary>Lote da importação que criou a unidade; nulo em cadastro manual.</summary>
    public Guid? LoteImportacao { get; set; }

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    public ICollection<RotationSchedule> Schedules { get; set; } = [];
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = [];
    public ICollection<StudentAllocation> Allocations { get; set; } = [];

    /// <summary>Endereço em uma linha, como vai ao geocodificador: número e cidade distinguem unidades na mesma via.</summary>
    public string EnderecoCompleto
    {
        get
        {
            var partes = new List<string>();
            var logradouro = string.Join(" ", new[] { Address, Numero }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
            if (!string.IsNullOrWhiteSpace(logradouro)) partes.Add(logradouro);
            if (!string.IsNullOrWhiteSpace(Bairro)) partes.Add(Bairro!);
            if (!string.IsNullOrWhiteSpace(Cidade)) partes.Add(Cidade!);
            if (!string.IsNullOrWhiteSpace(Uf)) partes.Add(Uf!);
            if (!string.IsNullOrWhiteSpace(Cep)) partes.Add(Cep!);
            return string.Join(", ", partes);
        }
    }

    /// <summary>Coordenadas definidas à mão nunca são sobrescritas automaticamente.</summary>
    public bool CoordenadaManual => OrigemCoordenadas == Models.OrigemCoordenadas.Manual;

    public bool TemCoordenadas => Latitude != 0 || Longitude != 0;

    /// <summary>
    /// Há coordenadas confirmadas (geocodificação com sucesso, CNES ou manual). Status nulo é
    /// cadastro anterior ao módulo, que sempre nasceu com coordenadas digitadas.
    /// </summary>
    public bool LocalizacaoConfirmada =>
        TemCoordenadas
        && (StatusGeocodificacao is null || StatusGeocodificacao == Models.StatusGeocodificacao.Sucesso);
}

/// <summary>Regra única de coordenada aceitável, usada por todo caminho de cadastro.</summary>
public static class Coordenadas
{
    /// <summary>Motivo da recusa, ou <c>null</c> quando o par é aceitável.</summary>
    public static string? Validar(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            return $"Latitude fora do intervalo válido (-90 a 90): {latitude}.";
        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            return $"Longitude fora do intervalo válido (-180 a 180): {longitude}.";
        // (0, 0) é o marcador de "sem coordenada" do cadastro: nunca uma unidade real.
        if (latitude == 0 && longitude == 0)
            return "Coordenadas (0, 0) não são válidas. Informe a localização real da unidade.";
        return null;
    }
}

/// <summary>
/// CNES com 7 dígitos e zeros à esquerda. A API do CNES devolve número ("10731") e a
/// planilha oficial traz "0010731": sem padronizar, a checagem de duplicidade falhava.
/// </summary>
public static class Cnes
{
    public const int Digitos = 7;

    /// <summary>Código padronizado, ou nulo quando vazio ou inválido (mais de 7 dígitos, letras).</summary>
    public static string? Normalizar(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;
        var texto = codigo.Trim();
        if (!texto.All(char.IsDigit) || texto.Length > Digitos) return null;
        return texto.PadLeft(Digitos, '0');
    }

    public static string Formatar(long codigo) => codigo.ToString($"D{Digitos}");

    /// <summary>Grafias possíveis no banco (oficial e sem zeros), até todo cadastro estar padronizado.</summary>
    public static string[] Variantes(string codigoNormalizado) =>
        [codigoNormalizado, codigoNormalizado.TrimStart('0')];
}

public static class StatusGeocodificacao
{
    public const string Pendente = "pendente";
    public const string Processando = "processando";
    public const string Sucesso = "sucesso";
    public const string NaoEncontrado = "nao_encontrado";
    public const string Erro = "erro";
    /// <summary>Encontrou algo, mas o resultado é duvidoso e precisa de conferência.</summary>
    public const string RevisaoManual = "revisao_manual";

    public static readonly string[] Todos =
        [Pendente, Processando, Sucesso, NaoEncontrado, Erro, RevisaoManual];

    public static bool Valido(string? valor) => valor != null && Todos.Contains(valor);
}

public static class OrigemCoordenadas
{
    public const string Nominatim = "NOMINATIM";
    public const string Manual = "MANUAL";
    public const string Outro = "OUTRO";

    public static readonly string[] Todas = [Nominatim, Manual, Outro];
}
