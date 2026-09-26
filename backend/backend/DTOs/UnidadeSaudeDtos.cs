using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

public class UnidadeSaudeDto
{
    public Guid Id { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? Tipo { get; init; }
    public string? Endereco { get; init; }
    public string? Numero { get; init; }
    public string? Complemento { get; init; }
    public string? Bairro { get; init; }
    public string? Cidade { get; init; }
    public string? Uf { get; init; }
    public string? Cep { get; init; }
    public string? Telefone { get; init; }
    public string EnderecoCompleto { get; init; } = string.Empty;

    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public bool TemCoordenadas { get; init; }
    public int RaioMetros { get; init; }
    public string? OrigemCoordenadas { get; init; }
    public string? StatusGeocodificacao { get; init; }
    public string? EnderecoGeocodificado { get; init; }
    public string? PrecisaoLocalizacao { get; init; }
    public DateTime? GeocodificadoEm { get; init; }

    public bool EhInstituicao { get; init; }
    public string? InicioTurno { get; init; }
    public string? FimTurno { get; init; }
    public string? CodigoCnes { get; init; }
    public bool Ativo { get; init; }

    public int EstagiariosAtivos { get; init; }

    public DateTime CriadoEm { get; init; }
    public DateTime AtualizadoEm { get; init; }
}

public record CriarUnidadeSaudeDto(
    [Required(ErrorMessage = "Informe o nome da unidade."), MaxLength(200)] string Nome,
    [MaxLength(100)] string? Tipo,
    [MaxLength(300)] string? Endereco,
    [MaxLength(20)] string? Numero,
    [MaxLength(200)] string? Complemento,
    [MaxLength(100)] string? Bairro,
    [MaxLength(100)] string? Cidade,
    [MaxLength(2), RegularExpression(FormatoUnidade.Uf, ErrorMessage = FormatoUnidade.UfMensagem)] string? Uf,
    [MaxLength(10), RegularExpression(FormatoUnidade.Cep, ErrorMessage = FormatoUnidade.CepMensagem)] string? Cep,
    [MaxLength(30)] string? Telefone,
    double? Latitude,
    double? Longitude,
    [Range(10, 5000, ErrorMessage = "O raio deve estar entre 10 e 5000 metros.")] int? RaioMetros,
    bool EhInstituicao = false,
    [MaxLength(5), RegularExpression(FormatoUnidade.Hora, ErrorMessage = "Início do turno inválido: use HH:mm (24h).")] string? InicioTurno = null,
    [MaxLength(5), RegularExpression(FormatoUnidade.Hora, ErrorMessage = "Fim do turno inválido: use HH:mm (24h).")] string? FimTurno = null,
    /// <summary>Geocodificar logo após criar. Ignorado se latitude/longitude vierem preenchidas.</summary>
    bool GeocodificarAgora = true
);

public record AtualizarUnidadeSaudeDto(
    [Required, MaxLength(200)] string Nome,
    [MaxLength(100)] string? Tipo,
    [MaxLength(300)] string? Endereco,
    [MaxLength(20)] string? Numero,
    [MaxLength(200)] string? Complemento,
    [MaxLength(100)] string? Bairro,
    [MaxLength(100)] string? Cidade,
    [MaxLength(2), RegularExpression(FormatoUnidade.Uf, ErrorMessage = FormatoUnidade.UfMensagem)] string? Uf,
    [MaxLength(10), RegularExpression(FormatoUnidade.Cep, ErrorMessage = FormatoUnidade.CepMensagem)] string? Cep,
    [MaxLength(30)] string? Telefone,
    [Range(10, 5000)] int? RaioMetros,
    bool EhInstituicao,
    [MaxLength(5), RegularExpression(FormatoUnidade.Hora, ErrorMessage = "Início do turno inválido: use HH:mm (24h).")] string? InicioTurno,
    [MaxLength(5), RegularExpression(FormatoUnidade.Hora, ErrorMessage = "Fim do turno inválido: use HH:mm (24h).")] string? FimTurno,
    bool? Ativo
);

public record DefinirCoordenadasDto(
    [Required, Range(-90, 90, ErrorMessage = "Latitude fora do intervalo válido.")] double Latitude,
    [Required, Range(-180, 180, ErrorMessage = "Longitude fora do intervalo válido.")] double Longitude,
    [MaxLength(500)] string? Observacao
);

public class GeocodificacaoRespostaDto
{
    public bool Sucesso { get; init; }
    public string Status { get; init; } = string.Empty;
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? EnderecoEncontrado { get; init; }
    public string? Precisao { get; init; }
    public string? Mensagem { get; init; }
    public bool VeioDoCache { get; init; }
}

public record PreverEnderecoDto(
    [MaxLength(200)] string? Nome,
    [MaxLength(300)] string? Endereco,
    [MaxLength(20)] string? Numero,
    [MaxLength(100)] string? Bairro,
    [MaxLength(100)] string? Cidade,
    [MaxLength(2), RegularExpression(FormatoUnidade.Uf, ErrorMessage = FormatoUnidade.UfMensagem)] string? Uf,
    [MaxLength(10)] string? Cep
);

public class ImportPreviewLinhaDto
{
    public int Linha { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? Tipo { get; init; }
    public string EnderecoResumo { get; init; } = string.Empty;
    public string? Cidade { get; init; }
    public string? Cep { get; init; }
    /// <summary>"valida" | "invalida" | "duplicada" | "duplicada_endereco_alterado"</summary>
    public string Status { get; init; } = string.Empty;
    public List<string> Erros { get; init; } = [];
    public Guid? UnidadeExistenteId { get; init; }
}

public class ImportPreviewDto
{
    public Guid PreviewId { get; init; }
    public int TotalLinhas { get; init; }
    public int Validas { get; init; }
    public int Invalidas { get; init; }
    public int Duplicadas { get; init; }
    /// <summary>Erros do arquivo como um todo; se houver, nada pode ser importado.</summary>
    public List<string> Erros { get; init; } = [];
    public List<ImportPreviewLinhaDto> Linhas { get; init; } = [];
    public bool PodeConfirmar { get; init; }
}

public record ConfirmarImportacaoDto(
    [Required] Guid PreviewId,
    /// <summary>"ignorar" (padrão) ou "atualizar" para as unidades já cadastradas.</summary>
    string? AcaoDuplicadas
);

public class ImportacaoResultadoDto
{
    public Guid LoteId { get; init; }
    public int Criadas { get; init; }
    public int Atualizadas { get; init; }
    public int Ignoradas { get; init; }
    public int EnfileiradasParaGeocodificar { get; init; }
    public string Mensagem { get; init; } = string.Empty;
}

public class ImportacaoProgressoDto
{
    public Guid LoteId { get; init; }
    public int Total { get; init; }
    public int Processados { get; init; }
    public int Pendentes { get; init; }
    public int Sucesso { get; init; }
    public int RevisaoManual { get; init; }
    public int NaoEncontrado { get; init; }
    public int Erro { get; init; }
    public int PercentualConcluido { get; init; }
    public bool Concluido { get; init; }
}

/// <summary>
/// Formatos aceitos no cadastro da unidade. O tamanho máximo sozinho deixava passar
/// "abc" como horário de turno e texto qualquer no CEP; a tela já aplica as mesmas
/// máscaras, isto protege quem chama a API direto. Vazio continua valendo (campo opcional).
/// </summary>
public static class FormatoUnidade
{
    public const string Cep = @"^\d{5}-?\d{3}$";
    public const string CepMensagem = "CEP inválido: use 8 dígitos (00000-000).";

    public const string Uf = "^[A-Za-z]{2}$";
    public const string UfMensagem = "UF inválida: use a sigla com 2 letras.";

    public const string Hora = "^([01][0-9]|2[0-3]):[0-5][0-9]$";
}
