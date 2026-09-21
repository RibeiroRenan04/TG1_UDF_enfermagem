using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>
/// Unidade de saúde onde o estágio acontece (tabela "Locais").
///
/// É a mesma entidade usada pelo geofence do check-in: as coordenadas gravadas
/// aqui — por geocodificação ou manualmente — são as que validam a presença do
/// aluno. Por isso o módulo de Unidades de Saúde estende esta tabela em vez de
/// criar uma segunda: duas fontes de coordenadas divergiriam com o tempo.
/// </summary>
public class Location
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>Logradouro. Ver também <see cref="EnderecoCompleto"/>.</summary>
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

    // ── Cadastro da unidade de saúde ──────────────────────────────────────────

    /// <summary>Tipo da unidade: "UBS", "Hospital", "UPA", "Instituição de ensino"…</summary>
    public string? Tipo { get; set; }

    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }

    /// <summary>Sigla da unidade federativa, ex: "DF".</summary>
    public string? Uf { get; set; }

    public string? Cep { get; set; }
    public string? Telefone { get; set; }

    /// <summary>Unidade inativa não recebe novas alocações nem aparece nas listas padrão.</summary>
    public bool Ativo { get; set; } = true;

    // ── Geocodificação ────────────────────────────────────────────────────────

    /// <summary>De onde vieram as coordenadas. Ver <see cref="OrigemCoordenadas"/>.</summary>
    public string? OrigemCoordenadas { get; set; }

    /// <summary>Situação da geocodificação. Ver <see cref="StatusGeocodificacao"/>.</summary>
    public string? StatusGeocodificacao { get; set; }

    /// <summary>Endereço que o provedor devolveu (display_name), para conferência.</summary>
    public string? EnderecoGeocodificado { get; set; }

    /// <summary>Precisão informada pelo provedor, ex: "building", "road", "suburb".</summary>
    public string? PrecisaoLocalizacao { get; set; }

    public DateTime? GeocodificadoEm { get; set; }

    /// <summary>
    /// Lote da importação que criou a unidade. Serve para acompanhar o progresso
    /// da geocodificação daquela planilha; nulo em cadastro manual.
    /// </summary>
    public Guid? LoteImportacao { get; set; }

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    // Navigation
    public ICollection<RotationSchedule> Schedules { get; set; } = [];
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = [];
    public ICollection<StudentAllocation> Allocations { get; set; } = [];

    /// <summary>
    /// Endereço em uma linha, do jeito que é enviado ao geocodificador. Inclui o
    /// número e a cidade porque são justamente eles que distinguem uma unidade de
    /// outra na mesma via.
    /// </summary>
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
    /// Localização em que o ponto pode confiar: há coordenadas e elas foram
    /// confirmadas (geocodificação com sucesso, CNES ou definição manual). É o
    /// mesmo critério da tela de revisão — o que está lá pendente, em revisão ou
    /// não encontrado não serve para validar o raio do check-in.
    /// Status nulo é cadastro anterior ao módulo de geocodificação, que sempre
    /// nasceu com coordenadas digitadas.
    /// </summary>
    public bool LocalizacaoConfirmada =>
        TemCoordenadas
        && (StatusGeocodificacao is null || StatusGeocodificacao == Models.StatusGeocodificacao.Sucesso);
}

/// <summary>
/// Regra única de coordenada aceitável para uma unidade, usada por todo caminho
/// de cadastro (formulário, revisão manual, importação, CNES). Sem ela, cada
/// tela validava de um jeito — e (0, 0) chegou a ser gravado como "sucesso".
/// </summary>
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
/// Código CNES no formato oficial: 7 dígitos, com zeros à esquerda.
///
/// A API do CNES devolve o código como número, e o "Buscar no CNES" o gravava sem
/// os zeros ("10731"); a planilha oficial traz "0010731". Os dois são a mesma
/// unidade, mas não se reconheciam — e a checagem de duplicidade por CNES
/// deixava passar a unidade repetida.
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

    /// <summary>
    /// As grafias com que o código pode estar gravado: a oficial e a antiga, sem
    /// os zeros. Serve para achar a unidade até que todo cadastro esteja padronizado.
    /// </summary>
    public static string[] Variantes(string codigoNormalizado) =>
        [codigoNormalizado, codigoNormalizado.TrimStart('0')];
}

/// <summary>Situação da geocodificação de uma unidade.</summary>
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

/// <summary>De onde vieram as coordenadas de uma unidade.</summary>
public static class OrigemCoordenadas
{
    public const string Nominatim = "NOMINATIM";
    public const string Manual = "MANUAL";
    public const string Outro = "OUTRO";

    public static readonly string[] Todas = [Nominatim, Manual, Outro];
}
