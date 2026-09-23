using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services.Geocoding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EstagioCheck.API.Services.Import;

/// <summary>
/// Lê a planilha, detecta duplicidades e guarda a prévia em memória; só a confirmação
/// grava no banco e enfileira a geocodificação.
/// </summary>
public class UnidadeImportService(
    AppDbContext db,
    PlanilhaUnidadesReader reader,
    IAddressNormalizer normalizer,
    GeocodingQueue fila,
    IMemoryCache cache,
    ILogger<UnidadeImportService> logger)
{
    private static readonly TimeSpan ValidadeDaPrevia = TimeSpan.FromMinutes(30);

    private static string ChaveDaPrevia(Guid id) => $"import_unidades_{id}";

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
    /// Duplicadas são ignoradas por padrão. Com <paramref name="atualizarDuplicadas"/> o cadastro
    /// é atualizado, mas coordenadas de origem MANUAL nunca são perdidas.
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

                if (linha.TemCoordenadas && !existente.CoordenadaManual)
                {
                    AplicarCoordenadasDaPlanilha(existente, linha, loteId);
                }
                else if (linha.EnderecoAlterado && !existente.CoordenadaManual)
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
                CodigoCnes = linha.CodigoCnes,
                Ativo = true,
                StatusGeocodificacao = StatusGeocodificacao.Pendente,
                LoteImportacao = loteId
            };

            db.Locations.Add(unidade);
            criadas++;

            if (linha.TemCoordenadas) AplicarCoordenadasDaPlanilha(unidade, linha, loteId);
            else paraGeocodificar.Add(unidade.Id);
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

    /// <summary>
    /// Nome sozinho não basta ("UBS 1" existe em várias regiões): a chave junta nome, logradouro,
    /// número e cidade normalizados.
    /// </summary>
    private async Task MarcarDuplicadasAsync(UnidadeImportResult resultado, CancellationToken ct)
    {
        var existentes = await db.Locations
            .Select(l => new
            {
                l.Id, l.Name, l.Address, l.Numero, l.Cidade, l.Bairro, l.Cep, l.CodigoCnes
            })
            .ToListAsync(ct);

        var indice = new Dictionary<string, Guid>();
        foreach (var e in existentes)
        {
            var chave = ChaveLogica(e.Name, e.Address, e.Numero, e.Cidade);
            indice.TryAdd(chave, e.Id);
        }

        // O CNES identifica a unidade sozinho e tem índice único: sem esta checagem, reimportar
        // a mesma unidade derrubava a confirmação. Normalizado dos dois lados ("10731" x "0010731").
        var porCnes = existentes
            .Select(e => new { e.Id, Cnes = Cnes.Normalizar(e.CodigoCnes) })
            .Where(e => e.Cnes != null)
            .GroupBy(e => e.Cnes!)
            .ToDictionary(g => g.Key, g => g.First().Id);
        var cnesNoArquivo = new Dictionary<string, int>();

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

            var cnes = linha.CodigoCnes?.Trim();
            if (!string.IsNullOrEmpty(cnes))
            {
                if (cnesNoArquivo.TryGetValue(cnes, out var linhaDoCnes))
                {
                    linha.Erros.Add($"Código CNES {cnes} repetido na planilha (já aparece na linha {linhaDoCnes}).");
                    continue;
                }
                cnesNoArquivo[cnes] = linha.Linha;
            }

            // O CNES vence a chave de nome/endereço: é o identificador oficial.
            Guid idExistente;
            if (!string.IsNullOrEmpty(cnes) && porCnes.TryGetValue(cnes, out var idPorCnes))
                idExistente = idPorCnes;
            else if (!indice.TryGetValue(chave, out idExistente))
                continue;

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
        unidade.CodigoCnes = linha.CodigoCnes ?? unidade.CodigoCnes;
    }

    /// <summary>Origem "OUTRO": não é o Nominatim nem correção manual (e é o que a restrição do banco aceita).</summary>
    private static void AplicarCoordenadasDaPlanilha(Location unidade, UnidadeImportRow linha, Guid loteId)
    {
        unidade.Latitude = linha.Latitude!.Value;
        unidade.Longitude = linha.Longitude!.Value;
        unidade.OrigemCoordenadas = OrigemCoordenadas.Outro;
        unidade.StatusGeocodificacao = StatusGeocodificacao.Sucesso;
        unidade.EnderecoGeocodificado = null;
        unidade.PrecisaoLocalizacao = "Coordenadas informadas na planilha";
        unidade.GeocodificadoEm = BrasiliaTime.Agora;
        unidade.LoteImportacao = loteId;
    }
}
