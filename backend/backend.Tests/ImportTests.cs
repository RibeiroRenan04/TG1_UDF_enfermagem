using System.Text;
using ClosedXML.Excel;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services.Geocoding;
using EstagioCheck.API.Services.Import;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>Leitura e validação da planilha de unidades.</summary>
public class PlanilhaReaderTests
{
    private static PlanilhaUnidadesReader Reader() =>
        new(new AddressNormalizer(), TestSupport.Logger<PlanilhaUnidadesReader>());

    private static Stream Xlsx(params string[][] linhas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Unidades");
        for (var l = 0; l < linhas.Length; l++)
            for (var c = 0; c < linhas[l].Length; c++)
                ws.Cell(l + 1, c + 1).Value = linhas[l][c];

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static Stream Csv(string conteudo) =>
        new MemoryStream(Encoding.UTF8.GetBytes(conteudo));

    private static readonly string[] Cabecalho =
        ["Nome", "Tipo", "Endereco", "Numero", "Complemento", "Bairro", "Cidade", "UF", "CEP", "Telefone"];

    [Fact]
    public void Planilha_valida_e_lida_por_completo()
    {
        using var arquivo = Xlsx(
            Cabecalho,
            ["UBS 1 Asa Norte", "UBS", "SGAN 906", "S/N", "", "Asa Norte", "Brasília", "DF", "70790-060", "(61) 3550-0000"],
            ["UBS 2 Asa Sul", "UBS", "SGAS 612", "S/N", "", "Asa Sul", "Brasília", "DF", "70200-720", ""]);

        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.False(r.Falhou);
        Assert.Equal(2, r.Linhas.Count);
        Assert.All(r.Linhas, l => Assert.True(l.Valida));
        Assert.Equal("UBS 1 Asa Norte", r.Linhas[0].Nome);
        Assert.Equal("DF", r.Linhas[0].Uf);
        Assert.Equal(2, r.Linhas[0].Linha); // linha 1 é o cabeçalho
    }

    [Fact]
    public void Csv_e_aceito_com_ponto_e_virgula()
    {
        using var arquivo = Csv(
            "Nome;Tipo;Endereco;Numero;Complemento;Bairro;Cidade;UF;CEP;Telefone\n" +
            "UBS 1;UBS;SGAN 906;S/N;;Asa Norte;Brasília;DF;70790-060;\n");

        var r = Reader().Ler(arquivo, "unidades.csv");

        Assert.False(r.Falhou);
        Assert.Single(r.Linhas);
        Assert.Equal("UBS 1", r.Linhas[0].Nome);
    }

    [Fact]
    public void Csv_respeita_aspas_no_endereco_com_virgula()
    {
        using var arquivo = Csv(
            "Nome,Endereco,Cidade\n" +
            "\"UBS 1, Bloco A\",\"SGAN 906, Lote 2\",Brasília\n");

        var r = Reader().Ler(arquivo, "unidades.csv");

        Assert.Single(r.Linhas);
        Assert.Equal("UBS 1, Bloco A", r.Linhas[0].Nome);
        Assert.Equal("SGAN 906, Lote 2", r.Linhas[0].Endereco);
    }

    [Fact]
    public void Extensao_nao_aceita_e_recusada()
    {
        using var arquivo = Csv("qualquer coisa");
        var r = Reader().Ler(arquivo, "unidades.txt");

        Assert.True(r.Falhou);
        Assert.Contains(r.ErrosGerais, e => e.Contains("Formato não aceito"));
    }

    [Fact]
    public void Conteudo_que_nao_corresponde_a_extensao_e_recusado()
    {
        // Renomear um .txt para .xlsx não deve enganar a importação.
        using var arquivo = Csv("Nome;Cidade\nUBS 1;Brasília\n");
        var r = Reader().Ler(arquivo, "disfarcado.xlsx");

        Assert.True(r.Falhou);
        Assert.Contains(r.ErrosGerais, e => e.Contains("não é um .xlsx válido"));
    }

    [Fact]
    public void Coluna_obrigatoria_ausente_e_recusada()
    {
        using var arquivo = Xlsx(["Endereco", "Cidade"], ["SGAN 906", "Brasília"]);
        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.True(r.Falhou);
        Assert.Contains(r.ErrosGerais, e => e.Contains("Coluna obrigatória ausente"));
    }

    [Fact]
    public void Planilha_sem_linhas_de_dados_e_recusada()
    {
        using var arquivo = Xlsx(Cabecalho);
        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.True(r.Falhou);
    }

    [Fact]
    public void Arquivo_vazio_e_recusado()
    {
        using var arquivo = new MemoryStream();
        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.True(r.Falhou);
        Assert.Contains(r.ErrosGerais, e => e.Contains("vazio"));
    }

    [Fact]
    public void Linha_sem_nome_e_invalida()
    {
        using var arquivo = Xlsx(
            ["Nome", "Endereco", "Cidade"],
            ["", "SGAN 906", "Brasília"]);

        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.Single(r.Linhas);
        Assert.False(r.Linhas[0].Valida);
        Assert.Contains(r.Linhas[0].Erros, e => e.Contains("Nome"));
    }

    [Fact]
    public void Cep_invalido_e_apontado_na_linha()
    {
        using var arquivo = Xlsx(
            ["Nome", "Endereco", "Cidade", "CEP"],
            ["UBS X", "SGAN 906", "Brasília", "123"]);

        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.False(r.Linhas[0].Valida);
        Assert.Contains(r.Linhas[0].Erros, e => e.Contains("CEP inválido"));
    }

    [Fact]
    public void Uf_invalida_e_apontada_na_linha()
    {
        using var arquivo = Xlsx(
            ["Nome", "Endereco", "Cidade", "UF"],
            ["UBS X", "SGAN 906", "Brasília", "Distrito Federal"]);

        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.Contains(r.Linhas[0].Erros, e => e.Contains("UF inválida"));
    }

    [Fact]
    public void Linha_sem_endereco_e_sem_cidade_e_invalida()
    {
        using var arquivo = Xlsx(["Nome", "Endereco", "Cidade"], ["UBS X", "", ""]);
        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.Contains(r.Linhas[0].Erros, e => e.Contains("endereço ou a cidade"));
    }

    [Fact]
    public void Celula_iniciada_por_igual_e_neutralizada()
    {
        // Injeção de fórmula: o valor não pode voltar executável para outra planilha.
        using var arquivo = Xlsx(
            ["Nome", "Endereco", "Cidade"],
            ["=HYPERLINK(\"http://x\")", "SGAN 906", "Brasília"]);

        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.StartsWith("'", r.Linhas[0].Nome);
    }

    [Fact]
    public void Linhas_totalmente_vazias_sao_ignoradas()
    {
        using var arquivo = Xlsx(
            ["Nome", "Endereco", "Cidade"],
            ["UBS 1", "SGAN 906", "Brasília"],
            ["", "", ""],
            ["UBS 2", "SGAS 612", "Brasília"]);

        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.Equal(2, r.Linhas.Count);
    }

    [Fact]
    public void Cabecalho_com_acento_e_maiuscula_e_reconhecido()
    {
        using var arquivo = Xlsx(
            ["NOME", "Endereço", "CIDADE"],
            ["UBS 1", "SGAN 906", "Brasília"]);

        var r = Reader().Ler(arquivo, "unidades.xlsx");

        Assert.False(r.Falhou);
        Assert.Equal("SGAN 906", r.Linhas[0].Endereco);
    }
}

/// <summary>Duplicidade e confirmação da importação.</summary>
public class UnidadeImportServiceTests
{
    private static UnidadeImportService Montar(
        EstagioCheck.API.Data.AppDbContext db, GeocodingQueue? fila = null) =>
        new(db,
            new PlanilhaUnidadesReader(new AddressNormalizer(), TestSupport.Logger<PlanilhaUnidadesReader>()),
            new AddressNormalizer(),
            fila ?? new GeocodingQueue(),
            new MemoryCache(new MemoryCacheOptions()),
            TestSupport.Logger<UnidadeImportService>());

    private static Stream Csv(string conteudo) => new MemoryStream(Encoding.UTF8.GetBytes(conteudo));

    private const string CabecalhoCsv = "Nome;Tipo;Endereco;Numero;Bairro;Cidade;UF;CEP\n";

    [Fact]
    public async Task Unidade_ja_cadastrada_e_marcada_como_duplicada()
    {
        using var db = TestSupport.NovoContexto();
        db.Locations.Add(TestSupport.Unidade("UBS 1 Asa Norte"));
        await db.SaveChangesAsync();

        using var arquivo = Csv(CabecalhoCsv +
            "UBS 1 Asa Norte;UBS;SGAN 906;S/N;Asa Norte;Brasília;DF;70790-060\n");

        var (r, _) = await Montar(db).GerarPreviaAsync(arquivo, "u.csv", default);

        Assert.Single(r.Linhas);
        Assert.True(r.Linhas[0].Duplicada);
        Assert.Equal("duplicada", r.Linhas[0].Status);
    }

    [Fact]
    public async Task Nome_parecido_em_endereco_diferente_nao_e_duplicata()
    {
        using var db = TestSupport.NovoContexto();
        db.Locations.Add(TestSupport.Unidade("UBS 1"));
        await db.SaveChangesAsync();

        // Mesmo nome, outra via e outra cidade: são unidades legítimas distintas.
        using var arquivo = Csv(CabecalhoCsv +
            "UBS 1;UBS;Quadra 200;10;Centro;Taguatinga;DF;72000-000\n");

        var (r, _) = await Montar(db).GerarPreviaAsync(arquivo, "u.csv", default);

        Assert.False(r.Linhas[0].Duplicada);
    }

    [Fact]
    public async Task Endereco_alterado_da_unidade_existente_e_sinalizado()
    {
        using var db = TestSupport.NovoContexto();
        db.Locations.Add(TestSupport.Unidade("UBS 1 Asa Norte"));
        await db.SaveChangesAsync();

        // Mesmo nome/via/número/cidade (mesma unidade), porém com o CEP corrigido.
        using var arquivo = Csv(CabecalhoCsv +
            "UBS 1 Asa Norte;UBS;SGAN 906;S/N;Asa Norte;Brasília;DF;70000-111\n");

        var (r, _) = await Montar(db).GerarPreviaAsync(arquivo, "u.csv", default);

        Assert.True(r.Linhas[0].Duplicada);
        Assert.True(r.Linhas[0].EnderecoAlterado);
        Assert.Equal("duplicada_endereco_alterado", r.Linhas[0].Status);
    }

    [Fact]
    public async Task Linha_repetida_dentro_da_propria_planilha_e_apontada()
    {
        using var db = TestSupport.NovoContexto();
        using var arquivo = Csv(CabecalhoCsv +
            "UBS 1;UBS;SGAN 906;S/N;Asa Norte;Brasília;DF;70790-060\n" +
            "UBS 1;UBS;SGAN 906;S/N;Asa Norte;Brasília;DF;70790-060\n");

        var (r, _) = await Montar(db).GerarPreviaAsync(arquivo, "u.csv", default);

        Assert.True(r.Linhas[0].Valida);
        Assert.False(r.Linhas[1].Valida);
        Assert.Contains(r.Linhas[1].Erros, e => e.Contains("repetida na planilha"));
    }

    [Fact]
    public async Task Previa_nao_grava_nada_no_banco()
    {
        using var db = TestSupport.NovoContexto();
        using var arquivo = Csv(CabecalhoCsv +
            "UBS 1;UBS;SGAN 906;S/N;Asa Norte;Brasília;DF;70790-060\n");

        await Montar(db).GerarPreviaAsync(arquivo, "u.csv", default);

        Assert.Empty(db.Locations);
    }

    [Fact]
    public async Task Confirmacao_cria_as_unidades_e_enfileira_geocodificacao()
    {
        using var db = TestSupport.NovoContexto();
        var fila = new GeocodingQueue();
        var service = Montar(db, fila);

        using var arquivo = Csv(CabecalhoCsv +
            "UBS 1;UBS;SGAN 906;S/N;Asa Norte;Brasília;DF;70790-060\n" +
            "UBS 2;UBS;SGAS 612;S/N;Asa Sul;Brasília;DF;70200-720\n");

        var (previa, _) = await service.GerarPreviaAsync(arquivo, "u.csv", default);
        var (loteId, criadas, atualizadas, ignoradas, enfileiradas) =
            await service.ConfirmarAsync(previa, atualizarDuplicadas: false, default);

        Assert.Equal(2, criadas);
        Assert.Equal(0, atualizadas);
        Assert.Equal(0, ignoradas);
        Assert.Equal(2, enfileiradas);
        Assert.Equal(2, db.Locations.Count());
        Assert.All(db.Locations, u => Assert.Equal(StatusGeocodificacao.Pendente, u.StatusGeocodificacao));
        Assert.All(db.Locations, u => Assert.Equal(loteId, u.LoteImportacao));
    }

    [Fact]
    public async Task Duplicada_e_ignorada_por_padrao()
    {
        using var db = TestSupport.NovoContexto();
        db.Locations.Add(TestSupport.Unidade("UBS 1 Asa Norte"));
        await db.SaveChangesAsync();

        var service = Montar(db);
        using var arquivo = Csv(CabecalhoCsv +
            "UBS 1 Asa Norte;UBS;SGAN 906;S/N;Asa Norte;Brasília;DF;70790-060\n");

        var (previa, _) = await service.GerarPreviaAsync(arquivo, "u.csv", default);
        var (_, criadas, atualizadas, ignoradas, _) =
            await service.ConfirmarAsync(previa, atualizarDuplicadas: false, default);

        Assert.Equal(0, criadas);
        Assert.Equal(0, atualizadas);
        Assert.Equal(1, ignoradas);
        Assert.Single(db.Locations);
    }

    [Fact]
    public async Task Reimportacao_nao_apaga_coordenada_definida_a_mao()
    {
        using var db = TestSupport.NovoContexto();
        var existente = TestSupport.Unidade("UBS 1 Asa Norte");
        existente.Latitude = -15.5;
        existente.Longitude = -47.5;
        existente.OrigemCoordenadas = OrigemCoordenadas.Manual;
        existente.StatusGeocodificacao = StatusGeocodificacao.Sucesso;
        db.Locations.Add(existente);
        await db.SaveChangesAsync();

        var service = Montar(db);
        // Endereço mudou na planilha, mas a coordenada foi corrigida à mão antes.
        using var arquivo = Csv(CabecalhoCsv +
            "UBS 1 Asa Norte;UBS;SGAN 906;S/N;Asa Norte;Brasília;DF;70000-999\n");

        var (previa, _) = await service.GerarPreviaAsync(arquivo, "u.csv", default);
        await service.ConfirmarAsync(previa, atualizarDuplicadas: true, default);

        Assert.Equal(-15.5, existente.Latitude, 4);
        Assert.Equal(OrigemCoordenadas.Manual, existente.OrigemCoordenadas);
        Assert.Equal(StatusGeocodificacao.Sucesso, existente.StatusGeocodificacao);
    }

    [Fact]
    public async Task Atualizar_duplicada_com_endereco_novo_zera_coordenada_automatica()
    {
        using var db = TestSupport.NovoContexto();
        var existente = TestSupport.Unidade("UBS 1 Asa Norte");
        existente.Latitude = -15.5;
        existente.Longitude = -47.5;
        existente.OrigemCoordenadas = OrigemCoordenadas.Nominatim;
        existente.StatusGeocodificacao = StatusGeocodificacao.Sucesso;
        db.Locations.Add(existente);
        await db.SaveChangesAsync();

        var service = Montar(db);
        using var arquivo = Csv(CabecalhoCsv +
            "UBS 1 Asa Norte;UBS;SGAN 906;S/N;Asa Norte;Brasília;DF;70000-999\n");

        var (previa, _) = await service.GerarPreviaAsync(arquivo, "u.csv", default);
        var (_, _, atualizadas, _, enfileiradas) =
            await service.ConfirmarAsync(previa, atualizarDuplicadas: true, default);

        Assert.Equal(1, atualizadas);
        Assert.Equal(1, enfileiradas);
        Assert.Equal(StatusGeocodificacao.Pendente, existente.StatusGeocodificacao);
        Assert.False(existente.TemCoordenadas);
    }

    [Fact]
    public async Task Linha_invalida_nao_e_importada()
    {
        using var db = TestSupport.NovoContexto();
        var service = Montar(db);

        using var arquivo = Csv(CabecalhoCsv +
            "UBS 1;UBS;SGAN 906;S/N;Asa Norte;Brasília;DF;70790-060\n" +
            ";UBS;SGAS 612;S/N;Asa Sul;Brasília;DF;70200-720\n");

        var (previa, _) = await service.GerarPreviaAsync(arquivo, "u.csv", default);
        var (_, criadas, _, _, _) = await service.ConfirmarAsync(previa, false, default);

        Assert.Equal(1, criadas);
        Assert.Single(db.Locations);
    }

    [Fact]
    public void Previa_expirada_nao_e_recuperada()
    {
        using var db = TestSupport.NovoContexto();
        var service = Montar(db);
        Assert.Null(service.RecuperarPrevia(Guid.NewGuid()));
    }
}
