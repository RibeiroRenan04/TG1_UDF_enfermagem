using System.Text;
using ClosedXML.Excel;
using EstagioCheck.API.Services.Geocoding;

namespace EstagioCheck.API.Services.Import;

/// <summary>
/// Lê e valida a planilha de unidades de saúde (.xlsx ou .csv).
///
/// A leitura é puramente estrutural: o conteúdo das células é tratado como texto,
/// nunca como fórmula, e nada é executado. O arquivo não é gravado em disco — é
/// lido do fluxo e descartado.
/// </summary>
public class PlanilhaUnidadesReader(IAddressNormalizer normalizer, ILogger<PlanilhaUnidadesReader> logger)
{
    /// <summary>Teto de linhas por planilha, para uma importação não travar o servidor.</summary>
    public const int MaxLinhas = 2000;

    /// <summary>Tamanho máximo do arquivo enviado.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    public static readonly string[] ExtensoesAceitas = [".xlsx", ".csv"];

    /// <summary>Assinatura de um .xlsx (ZIP): "PK\x03\x04".</summary>
    private static readonly byte[] AssinaturaZip = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>Colunas obrigatórias no cabeçalho.</summary>
    public static readonly string[] ColunasObrigatorias = ["nome"];

    /// <summary>Cabeçalho oficial do modelo distribuído aos usuários.</summary>
    public static readonly string[] ColunasModelo =
        ["Nome", "Tipo", "Endereco", "Numero", "Complemento", "Bairro", "Cidade", "UF", "CEP", "Telefone"];

    public UnidadeImportResult Ler(Stream conteudo, string nomeArquivo)
    {
        var resultado = new UnidadeImportResult();

        var extensao = Path.GetExtension(nomeArquivo).ToLowerInvariant();
        if (!ExtensoesAceitas.Contains(extensao))
        {
            resultado.ErrosGerais.Add($"Formato não aceito ({extensao}). Envie um arquivo .xlsx ou .csv.");
            return resultado;
        }

        // A extensão sozinha não prova nada: conferimos a assinatura do arquivo.
        using var buffer = new MemoryStream();
        conteudo.CopyTo(buffer);
        buffer.Position = 0;

        if (buffer.Length == 0)
        {
            resultado.ErrosGerais.Add("O arquivo enviado está vazio.");
            return resultado;
        }

        var ehZip = ComecaCom(buffer, AssinaturaZip);
        buffer.Position = 0;

        if (extensao == ".xlsx" && !ehZip)
        {
            resultado.ErrosGerais.Add("O arquivo não é um .xlsx válido (conteúdo não corresponde à extensão).");
            return resultado;
        }
        if (extensao == ".csv" && ehZip)
        {
            resultado.ErrosGerais.Add("O arquivo tem extensão .csv mas o conteúdo é de uma planilha compactada.");
            return resultado;
        }

        try
        {
            var linhas = extensao == ".xlsx" ? LerXlsx(buffer, resultado) : LerCsv(buffer, resultado);
            if (resultado.Falhou) return resultado;

            foreach (var linha in linhas)
                resultado.Linhas.Add(linha);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao ler a planilha de unidades enviada.");
            resultado.ErrosGerais.Add("Não foi possível ler o arquivo. Verifique se ele não está corrompido.");
        }

        if (!resultado.Falhou && resultado.Linhas.Count == 0)
            resultado.ErrosGerais.Add("A planilha não possui nenhuma linha de dados.");

        return resultado;
    }

    // ── .xlsx ─────────────────────────────────────────────────────────────────
    private List<UnidadeImportRow> LerXlsx(Stream conteudo, UnidadeImportResult resultado)
    {
        using var workbook = new XLWorkbook(conteudo);
        var planilha = workbook.Worksheets.FirstOrDefault();
        if (planilha == null)
        {
            resultado.ErrosGerais.Add("A planilha não possui nenhuma aba.");
            return [];
        }

        var usadas = planilha.RangeUsed();
        if (usadas == null)
        {
            resultado.ErrosGerais.Add("A planilha está vazia.");
            return [];
        }

        var linhasPlanilha = usadas.RowsUsed().ToList();
        if (linhasPlanilha.Count < 2)
        {
            resultado.ErrosGerais.Add("A planilha precisa de um cabeçalho e ao menos uma linha de dados.");
            return [];
        }

        var cabecalho = linhasPlanilha[0].Cells().Select(c => Chave(c.GetString())).ToList();
        if (!ValidarCabecalho(cabecalho, resultado)) return [];

        var linhas = new List<UnidadeImportRow>();
        foreach (var linhaPlanilha in linhasPlanilha.Skip(1))
        {
            if (linhas.Count >= MaxLinhas)
            {
                resultado.ErrosGerais.Add(
                    $"A planilha excede o limite de {MaxLinhas} linhas. Divida a importação em partes.");
                return [];
            }

            var valores = new Dictionary<string, string>();
            for (var i = 0; i < cabecalho.Count; i++)
            {
                // GetString devolve o valor exibido; fórmulas nunca são avaliadas aqui.
                var celula = linhaPlanilha.Cell(i + 1);
                valores[cabecalho[i]] = celula.GetString().Trim();
            }

            var numeroLinha = linhaPlanilha.RowNumber();
            if (valores.Values.All(string.IsNullOrWhiteSpace)) continue;

            linhas.Add(MontarLinha(valores, numeroLinha));
        }

        return linhas;
    }

    // ── .csv ──────────────────────────────────────────────────────────────────
    private List<UnidadeImportRow> LerCsv(Stream conteudo, UnidadeImportResult resultado)
    {
        using var leitor = new StreamReader(conteudo, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var texto = leitor.ReadToEnd();
        var todasAsLinhas = texto.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (todasAsLinhas.Count < 2)
        {
            resultado.ErrosGerais.Add("O CSV precisa de um cabeçalho e ao menos uma linha de dados.");
            return [];
        }

        var separador = DetectarSeparador(todasAsLinhas[0]);
        var cabecalho = DividirCsv(todasAsLinhas[0], separador).Select(Chave).ToList();
        if (!ValidarCabecalho(cabecalho, resultado)) return [];

        var linhas = new List<UnidadeImportRow>();
        for (var i = 1; i < todasAsLinhas.Count; i++)
        {
            if (linhas.Count >= MaxLinhas)
            {
                resultado.ErrosGerais.Add(
                    $"O arquivo excede o limite de {MaxLinhas} linhas. Divida a importação em partes.");
                return [];
            }

            var colunas = DividirCsv(todasAsLinhas[i], separador);
            var valores = new Dictionary<string, string>();
            for (var c = 0; c < cabecalho.Count; c++)
                valores[cabecalho[c]] = c < colunas.Count ? colunas[c].Trim() : string.Empty;

            if (valores.Values.All(string.IsNullOrWhiteSpace)) continue;

            // +1 porque a primeira linha do arquivo é o cabeçalho.
            linhas.Add(MontarLinha(valores, i + 1));
        }

        return linhas;
    }

    private static char DetectarSeparador(string cabecalho) =>
        cabecalho.Count(c => c == ';') > cabecalho.Count(c => c == ',') ? ';' : ',';

    /// <summary>Divisão que respeita aspas, para endereços que contenham o separador.</summary>
    private static List<string> DividirCsv(string linha, char separador)
    {
        var colunas = new List<string>();
        var atual = new StringBuilder();
        var dentroDeAspas = false;

        for (var i = 0; i < linha.Length; i++)
        {
            var ch = linha[i];
            if (ch == '"')
            {
                if (dentroDeAspas && i + 1 < linha.Length && linha[i + 1] == '"') { atual.Append('"'); i++; }
                else dentroDeAspas = !dentroDeAspas;
            }
            else if (ch == separador && !dentroDeAspas)
            {
                colunas.Add(atual.ToString());
                atual.Clear();
            }
            else atual.Append(ch);
        }
        colunas.Add(atual.ToString());
        return colunas;
    }

    // ── Validação ─────────────────────────────────────────────────────────────
    private bool ValidarCabecalho(List<string> cabecalho, UnidadeImportResult resultado)
    {
        var faltando = ColunasObrigatorias.Where(c => !cabecalho.Contains(c)).ToList();
        if (faltando.Count > 0)
        {
            resultado.ErrosGerais.Add(
                $"Coluna obrigatória ausente: {string.Join(", ", faltando)}. " +
                $"O cabeçalho esperado é: {string.Join(", ", ColunasModelo)}.");
            return false;
        }
        return true;
    }

    private UnidadeImportRow MontarLinha(Dictionary<string, string> valores, int numeroLinha)
    {
        string? Campo(params string[] nomes)
        {
            foreach (var nome in nomes)
                if (valores.TryGetValue(nome, out var v) && !string.IsNullOrWhiteSpace(v))
                    return Sanitizar(v);
            return null;
        }

        var linha = new UnidadeImportRow
        {
            Linha = numeroLinha,
            Nome = Campo("nome", "unidade", "nomeunidade") ?? string.Empty,
            Tipo = Campo("tipo", "tipounidade"),
            Endereco = Campo("endereco", "logradouro", "rua"),
            Numero = Campo("numero", "num", "n"),
            Complemento = Campo("complemento"),
            Bairro = Campo("bairro"),
            Cidade = Campo("cidade", "municipio"),
            Uf = Campo("uf", "estado"),
            Cep = Campo("cep"),
            Telefone = Campo("telefone", "fone", "contato")
        };

        Validar(linha);
        return linha;
    }

    private void Validar(UnidadeImportRow linha)
    {
        if (string.IsNullOrWhiteSpace(linha.Nome))
            linha.Erros.Add("Nome da unidade é obrigatório.");
        else if (linha.Nome.Length > 200)
            linha.Erros.Add("Nome da unidade excede 200 caracteres.");

        // Sem endereço nem cidade não há como geocodificar nada de útil.
        if (string.IsNullOrWhiteSpace(linha.Endereco) && string.IsNullOrWhiteSpace(linha.Cidade))
            linha.Erros.Add("Informe ao menos o endereço ou a cidade da unidade.");

        if (!string.IsNullOrWhiteSpace(linha.Cep))
        {
            var cep = normalizer.NormalizarCep(linha.Cep);
            if (cep == null) linha.Erros.Add($"CEP inválido: \"{linha.Cep}\".");
            else linha.Cep = cep;
        }

        if (!string.IsNullOrWhiteSpace(linha.Uf))
        {
            var uf = normalizer.NormalizarUf(linha.Uf);
            if (uf == null) linha.Erros.Add($"UF inválida: \"{linha.Uf}\".");
            else linha.Uf = uf;
        }

        if (linha.Endereco?.Length > 300) linha.Erros.Add("Endereço excede 300 caracteres.");
        if (linha.Cidade?.Length > 100) linha.Erros.Add("Cidade excede 100 caracteres.");
        if (linha.Telefone?.Length > 30) linha.Erros.Add("Telefone excede 30 caracteres.");
    }

    /// <summary>
    /// Remove caracteres de controle e neutraliza células que o Excel interpretaria
    /// como fórmula ao reabrir o arquivo exportado (injeção de fórmula em CSV).
    /// </summary>
    private static string Sanitizar(string valor)
    {
        var limpo = new string(valor.Where(c => !char.IsControl(c) || c == ' ').ToArray()).Trim();
        return limpo.Length > 0 && "=+-@".Contains(limpo[0]) ? "'" + limpo : limpo;
    }

    /// <summary>Chave do cabeçalho: minúscula, sem acento, sem espaço nem pontuação.</summary>
    private string Chave(string cabecalho) => normalizer.Normalizar(cabecalho).Replace(" ", string.Empty);

    private static bool ComecaCom(Stream stream, byte[] assinatura)
    {
        if (stream.Length < assinatura.Length) return false;
        var inicio = new byte[assinatura.Length];
        var lidos = stream.Read(inicio, 0, assinatura.Length);
        return lidos == assinatura.Length && inicio.SequenceEqual(assinatura);
    }
}
