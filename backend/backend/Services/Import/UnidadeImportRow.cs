namespace EstagioCheck.API.Services.Import;

/// <summary>Uma linha da planilha de unidades, já lida e validada.</summary>
public class UnidadeImportRow
{
    /// <summary>Número da linha na planilha (1 é o cabeçalho), para o usuário achar o erro.</summary>
    public int Linha { get; set; }

    public string Nome { get; set; } = string.Empty;
    public string? Tipo { get; set; }
    public string? Endereco { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? Cep { get; set; }
    public string? Telefone { get; set; }

    /// <summary>
    /// Coordenadas informadas na própria planilha (opcionais, sempre em par). Vindas
    /// de fonte oficial como o CNES, dispensam a geocodificação pelo endereço — que
    /// é lenta (uma unidade por segundo) e erra com endereços de Brasília.
    /// </summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Código CNES da unidade (opcional). Também identifica duplicidade.</summary>
    public string? CodigoCnes { get; set; }

    public bool TemCoordenadas => Latitude.HasValue && Longitude.HasValue;

    public List<string> Erros { get; } = [];

    /// <summary>Unidade equivalente já cadastrada, quando houver.</summary>
    public Guid? UnidadeExistenteId { get; set; }
    public bool Duplicada => UnidadeExistenteId.HasValue;

    /// <summary>O endereço da planilha difere do que está gravado na unidade existente.</summary>
    public bool EnderecoAlterado { get; set; }

    public bool Valida => Erros.Count == 0;

    public string Status => !Valida ? "invalida"
        : Duplicada ? (EnderecoAlterado ? "duplicada_endereco_alterado" : "duplicada")
        : "valida";
}

/// <summary>Resultado da leitura de uma planilha inteira.</summary>
public class UnidadeImportResult
{
    public List<UnidadeImportRow> Linhas { get; } = [];

    /// <summary>Erros da planilha como um todo (arquivo ilegível, coluna faltando…).</summary>
    public List<string> ErrosGerais { get; } = [];

    public bool Falhou => ErrosGerais.Count > 0;
}
