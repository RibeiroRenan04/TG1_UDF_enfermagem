using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services.Geocoding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EstagioCheck.API.Services.Import;

/// <summary>
/// Orquestra a importação: lê a planilha, detecta duplicidades, guarda a prévia
/// e, na confirmação, cria/atualiza as unidades e as enfileira para geocodificação.
///
/// A gravação só acontece na confirmação. A prévia fica em memória por tempo curto,
/// para o usuário conferir antes de qualquer escrita no banco.
/// </summary>
public class UnidadeImportService(
    AppDbContext db,
    PlanilhaUnidadesReader reader,
    IAddressNormalizer normalizer,
    GeocodingQueue fila,
    IMemoryCache cache,
    ILogger<UnidadeImportService> logger)
{
    /// <summary>Prazo para confirmar uma prévia antes de precisar reenviar o arquivo.</summary>
    private static readonly TimeSpan ValidadeDaPrevia = TimeSpan.FromMinutes(30);

    private static string ChaveDaPrevia(Guid id) => $"import_unidades_{id}";

    /// <summary>Lê e valida a planilha, sem gravar nada.</summary>
    public async Task<(UnidadeImportResult resultado, Guid previewId)> GerarPreviaAsync(
        Stream conteudo, string nomeArquivo, CancellationToken ct)
    {
        logger.LogInformation("Importação de unidades iniciada a partir do arquivo {Arquivo}.", nomeArquivo);

        var resultado = reader.Ler(conteudo, nomeArquivo);
        if (resultado.Falhou)
        {
            logger.LogWarning("Planilha de unidades recusada: {Erros}", string.Join("; ", resultado.ErrosGerais));
            return (resultado, Guid.Empty);
        }

        await MarcarDuplicadasAsync(resultado, ct);

        var previewId = Guid.NewGuid();
        cache.Set(ChaveDaPrevia(previewId), resultado, ValidadeDaPrevia);

        logger.LogInformation(
            "Planilha validada: {Total} linha(s), {Validas} válida(s), {Invalidas} inválida(s), {Duplicadas} duplicada(s).",
            resultado.Linhas.Count,
            resultado.Linhas.Count(l => l.Valida && !l.Duplicada),
            resultado.Linhas.Count(l => !l.Valida),
            resultado.Linhas.Count(l => l.Duplicada));

        return (resultado, previewId);
    }

    public UnidadeImportResult? RecuperarPrevia(Guid previewId) =>
        cache.TryGetValue(ChaveDaPrevia(previewId), out UnidadeImportResult? r) ? r : null;

    public void DescartarPrevia(Guid previewId) => cache.Remove(ChaveDaPrevia(previewId));

    /// <summary>
    /// Cria as unidades da prévia e as enfileira para geocodificação.
    ///
    /// Duplicadas são ignoradas por padrão. Com <paramref name="atualizarDuplicadas"/>
    /// o cadastro é atualizado, mas coordenadas de origem MANUAL nunca são perdidas:
    /// uma correção feita à mão vale mais que o endereço de uma planilha.
    /// </summary>
    public async Task<(Guid loteId, int criadas, int atualizadas, int ignoradas, int enfileiradas)>
        ConfirmarAsync(UnidadeImportResult previa, bool atualizarDuplicadas, CancellationToken ct)
    {
        var loteId = Guid.NewGuid();
        int criadas = 0, atualizadas = 0, ignoradas = 0;
        var paraGeocodificar = new List<Guid>();

        foreach (var linha in previa.Linhas.Where(l => l.Valida))
        {
            if (linha.Duplicada)
            {
                if (!atualizarDuplicadas) { ignoradas++; continue; }

                var existente = await db.Locations.FirstOrDefaultAsync(l => l.Id == linha.UnidadeExistenteId, ct);
                if (existente == null) { ignoradas++; continue; }

                AtualizarCadastro(existente, linha);

                // Endereço mudou → as coordenadas antigas não valem mais, exceto as manuais.
                if (linha.EnderecoAlterado && !existente.CoordenadaManual)
                {
                    existente.Latitude = 0;
                    existente.Longitude = 0;
                    existente.EnderecoGeocodificado = null;
                    existente.PrecisaoLocalizacao = null;
                    existente.GeocodificadoEm = null;
                    existente.StatusGeocodificacao = StatusGeocodificacao.Pendente;
                    existente.LoteImportacao = loteId;
                    paraGeocodificar.Add(existente.Id);
                }

                existente.UpdatedAt = BrasiliaTime.Agora;
                atualizadas++;
                continue;
            }

            var unidade = new Location
            {
                Name = linha.Nome,
                Tipo = linha.Tipo,
                Address = linha.Endereco,
                Numero = linha.Numero,
                Complemento = linha.Complemento,
                Bairro = linha.Bairro,
                Cidade = linha.Cidade,
                Uf = linha.Uf,
                Cep = linha.Cep,
                Telefone = linha.Telefone,
                Ativo = true,
                StatusGeocodificacao = StatusGeocodificacao.Pendente,
                LoteImportacao = loteId
            };

            db.Locations.Add(unidade);
            paraGeocodificar.Add(unidade.Id);
            criadas++;
        }

        await db.SaveChangesAsync(ct);

        // Enfileira só depois de gravar: o serviço em segundo plano busca por id.
        foreach (var id in paraGeocodificar)
            fila.Enfileirar(id, loteId);

        logger.LogInformation(
            "Importação {LoteId} confirmada: {Criadas} criada(s), {Atualizadas} atualizada(s), " +
            "{Ignoradas} ignorada(s), {Fila} enfileirada(s) para geocodificação.",
            loteId, criadas, atualizadas, ignoradas, paraGeocodificar.Count);

        return (loteId, criadas, atualizadas, ignoradas, paraGeocodificar.Count);
    }

    // ── Duplicidade ───────────────────────────────────────────────────────────
    /// <summary>
    /// Marca as linhas que já existem no banco.
    ///
    /// Nome sozinho não basta: "UBS 1" existe em várias regiões. A chave junta nome,
    /// logradouro, número e cidade normalizados — específica o bastante para não
    /// confundir duas unidades legítimas de nome parecido, e tolerante à variação de
    /// acento e pontuação entre planilhas.
    /// </summary>
    private async Task MarcarDuplicadasAsync(UnidadeImportResult resultado, CancellationToken ct)
    {
        var existentes = await db.Locations
            .Select(l => new
            {
                l.Id, l.Name, l.Address, l.Numero, l.Cidade, l.Bairro, l.Cep
            })
            .ToListAsync(ct);

        var indice = new Dictionary<string, Guid>();
        foreach (var e in existentes)
        {
            var chave = ChaveLogica(e.Name, e.Address, e.Numero, e.Cidade);
            indice.TryAdd(chave, e.Id);
        }

        var enderecoAtual = existentes.ToDictionary(
            e => e.Id,
            e => normalizer.Normalizar($"{e.Address} {e.Numero} {e.Bairro} {e.Cidade} {e.Cep}"));

        // Duplicidade dentro da própria planilha conta como já vista.
        var vistasNoArquivo = new Dictionary<string, int>();

        foreach (var linha in resultado.Linhas.Where(l => l.Valida))
        {
            var chave = ChaveLogica(linha.Nome, linha.Endereco, linha.Numero, linha.Cidade);

            if (vistasNoArquivo.TryGetValue(chave, out var linhaAnterior))
            {
                linha.Erros.Add($"Unidade repetida na planilha (já aparece na linha {linhaAnterior}).");
                continue;
            }
            vistasNoArquivo[chave] = linha.Linha;

            if (!indice.TryGetValue(chave, out var idExistente)) continue;

            linha.UnidadeExistenteId = idExistente;
            var novoEndereco = normalizer.Normalizar(
                $"{linha.Endereco} {linha.Numero} {linha.Bairro} {linha.Cidade} {linha.Cep}");
            linha.EnderecoAlterado = enderecoAtual.TryGetValue(idExistente, out var atual)
                                  && atual != novoEndereco;
        }
    }

    private string ChaveLogica(string? nome, string? endereco, string? numero, string? cidade)
    {
        var n = normalizer.Normalizar(nome);
        var e = normalizer.Normalizar(endereco);
        var num = normalizer.Normalizar(numero);
        var c = normalizer.Normalizar(cidade);
        return $"{n}|{e}|{num}|{c}";
    }

    private static void AtualizarCadastro(Location unidade, UnidadeImportRow linha)
    {
        unidade.Name = linha.Nome;
        unidade.Tipo = linha.Tipo ?? unidade.Tipo;
        unidade.Address = linha.Endereco ?? unidade.Address;
        unidade.Numero = linha.Numero ?? unidade.Numero;
        unidade.Complemento = linha.Complemento ?? unidade.Complemento;
        unidade.Bairro = linha.Bairro ?? unidade.Bairro;
        unidade.Cidade = linha.Cidade ?? unidade.Cidade;
        unidade.Uf = linha.Uf ?? unidade.Uf;
        unidade.Cep = linha.Cep ?? unidade.Cep;
        unidade.Telefone = linha.Telefone ?? unidade.Telefone;
    }
}
