using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

/// <summary>Unidade de saúde na listagem e no detalhe.</summary>
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

    /// <summary>Estagiários com alocação ativa nesta unidade.</summary>
    public int EstagiariosAtivos { get; init; }

    public DateTime CriadoEm { get; init; }
    public DateTime AtualizadoEm { get; init; }
}

// ── Cadastro manual ───────────────────────────────────────────────────────────
public record CriarUnidadeSaudeDto(
    [Required(ErrorMessage = "Informe o nome da unidade."), MaxLength(200)] string Nome,
    [MaxLength(100)] string? Tipo,
    [MaxLength(300)] string? Endereco,
    [MaxLength(20)] string? Numero,
    [MaxLength(200)] string? Complemento,
    [MaxLength(100)] string? Bairro,
    [MaxLength(100)] string? Cidade,
    [MaxLength(2)] string? Uf,
    [MaxLength(10)] string? Cep,
    [MaxLength(30)] string? Telefone,
    double? Latitude,
    double? Longitude,
    [Range(10, 5000, ErrorMessage = "O raio deve estar entre 10 e 5000 metros.")] int? RaioMetros,
    bool EhInstituicao = false,
    [MaxLength(5)] string? InicioTurno = null,
    [MaxLength(5)] string? FimTurno = null,
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
    [MaxLength(2)] string? Uf,
    [MaxLength(10)] string? Cep,
    [MaxLength(30)] string? Telefone,
    [Range(10, 5000)] int? RaioMetros,
    bool EhInstituicao,
    [MaxLength(5)] string? InicioTurno,
    [MaxLength(5)] string? FimTurno,
    bool? Ativo
);

/// <summary>Ajuste manual das coordenadas na tela de revisão.</summary>
public record DefinirCoordenadasDto(
    [Required, Range(-90, 90, ErrorMessage = "Latitude fora do intervalo válido.")] double Latitude,
    [Required, Range(-180, 180, ErrorMessage = "Longitude fora do intervalo válido.")] double Longitude,
    [MaxLength(500)] string? Observacao
);

// ── Geocodificação ────────────────────────────────────────────────────────────
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

/// <summary>Prévia de um endereço avulso, antes de salvar a unidade.</summary>
public record PreverEnderecoDto(
    [MaxLength(200)] string? Nome,
    [MaxLength(300)] string? Endereco,
    [MaxLength(20)] string? Numero,
    [MaxLength(100)] string? Bairro,
    [MaxLength(100)] string? Cidade,
    [MaxLength(2)] string? Uf,
    [MaxLength(10)] string? Cep
);

// ── Importação ────────────────────────────────────────────────────────────────
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
    /// <summary>Token da prévia, devolvido na confirmação para importar o mesmo conteúdo.</summary>
    public Guid PreviewId { get; init; }
    public int TotalLinhas { get; init; }
    public int Validas { get; init; }
    public int Invalidas { get; init; }
    public int Duplicadas { get; init; }
    /// <summary>Erros do arquivo como um todo; se houver, nada pode ser importado.</summary>
    public List<string> Erros { get; init; } = [];
    public List<ImportPreviewLinhaDto> Linhas { get; init; } = [];
    /// <summary>Só é possível confirmar quando há ao menos uma linha aproveitável.</summary>
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
