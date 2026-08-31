using EstagioCheck.API.Models;
using EstagioCheck.API.Services.Geocoding;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>Regras em volta do provedor de geocodificação: cache, erros e revisão.</summary>
public class GeocodingTests
{
    private static UnitGeocoder Montar(EstagioCheck.API.Data.AppDbContext db, IGeocodingService provedor) =>
        new(db, provedor, new AddressNormalizer(), TestSupport.Logger<UnitGeocoder>());

    [Fact]
    public async Task Endereco_encontrado_grava_coordenadas_e_origem()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        var resultado = await Montar(db, GeocodificadorFalso.Encontra()).GeocodificarAsync(unidade);
        await db.SaveChangesAsync();

        Assert.True(resultado.Sucesso);
        Assert.Equal(StatusGeocodificacao.Sucesso, unidade.StatusGeocodificacao);
        Assert.Equal(-15.7401, unidade.Latitude, 4);
        Assert.Equal(OrigemCoordenadas.Nominatim, unidade.OrigemCoordenadas);
        Assert.NotNull(unidade.GeocodificadoEm);
    }

    [Fact]
    public async Task Endereco_nao_encontrado_marca_status_e_nao_inventa_coordenada()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        var resultado = await Montar(db, GeocodificadorFalso.NaoEncontra()).GeocodificarAsync(unidade);
        await db.SaveChangesAsync();

        Assert.False(resultado.Sucesso);
        Assert.Equal(StatusGeocodificacao.NaoEncontrado, unidade.StatusGeocodificacao);
        Assert.False(unidade.TemCoordenadas);
    }

    [Fact]
    public async Task Resultado_duvidoso_vai_para_revisao_manual()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        await Montar(db, GeocodificadorFalso.Encontra(duvidoso: true)).GeocodificarAsync(unidade);
        await db.SaveChangesAsync();

        Assert.Equal(StatusGeocodificacao.RevisaoManual, unidade.StatusGeocodificacao);
        // A coordenada é aproveitada, mas fica sinalizada para conferência.
        Assert.True(unidade.TemCoordenadas);
    }

    [Fact]
    public async Task Timeout_marca_erro_e_nao_entra_no_cache()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        var resultado = await Montar(db, GeocodificadorFalso.Falha()).GeocodificarAsync(unidade);
        await db.SaveChangesAsync();

        Assert.Equal(StatusGeocodificacao.Erro, unidade.StatusGeocodificacao);
        // Falha de comunicação não é resposta do serviço: cachear impediria nova tentativa.
        Assert.Empty(db.GeocodingCache);
        Assert.Contains("mais tarde", resultado.Mensagem);
    }

    [Fact]
    public async Task Http_429_marca_erro_com_mensagem_de_limite()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        var resultado = await Montar(db, GeocodificadorFalso.Falha(limiteExcedido: true))
            .GeocodificarAsync(unidade);

        Assert.Equal(StatusGeocodificacao.Erro, unidade.StatusGeocodificacao);
        Assert.Contains("Limite de uso", resultado.Mensagem);
    }

    [Fact]
    public async Task Cache_evita_segunda_consulta_ao_provedor()
    {
        using var db = TestSupport.NovoContexto();
        var primeira = TestSupport.Unidade();
        var segunda = TestSupport.Unidade(); // mesmo endereço, outra unidade
        db.Locations.AddRange(primeira, segunda);
        await db.SaveChangesAsync();

        var provedor = GeocodificadorFalso.Encontra();
        var geocoder = Montar(db, provedor);

        var r1 = await geocoder.GeocodificarAsync(primeira);
        await db.SaveChangesAsync();
        var r2 = await geocoder.GeocodificarAsync(segunda);
        await db.SaveChangesAsync();

        Assert.False(r1.VeioDoCache);
        Assert.True(r2.VeioDoCache);
        Assert.Equal(1, provedor.Chamadas); // o serviço público só foi consultado uma vez
        Assert.Equal(primeira.Latitude, segunda.Latitude, 6);
    }

    [Fact]
    public async Task Unidades_de_nomes_diferentes_no_mesmo_endereco_compartilham_o_cache()
    {
        // Anexos de um mesmo complexo: o nome entra na consulta para ajudar o
        // provedor, mas a chave do cache é o endereço — senão cada anexo geraria
        // uma consulta nova ao serviço público pelo mesmo lugar.
        using var db = TestSupport.NovoContexto();
        var anexoI = TestSupport.Unidade("Anexo I do Complexo");
        var anexoII = TestSupport.Unidade("Anexo II do Complexo");
        db.Locations.AddRange(anexoI, anexoII);
        await db.SaveChangesAsync();

        var provedor = GeocodificadorFalso.Encontra();
        var geocoder = Montar(db, provedor);

        await geocoder.GeocodificarAsync(anexoI);
        await db.SaveChangesAsync();
        var segundo = await geocoder.GeocodificarAsync(anexoII);
        await db.SaveChangesAsync();

        Assert.True(segundo.VeioDoCache);
        Assert.Equal(1, provedor.Chamadas);
        Assert.Single(db.GeocodingCache);
        Assert.Equal(anexoI.Latitude, anexoII.Latitude, 6);
    }

    [Fact]
    public async Task Unidade_sem_endereco_nao_polui_o_cache_com_o_nome()
    {
        // Só o nome é chave fraca demais para ser compartilhada entre unidades.
        using var db = TestSupport.NovoContexto();
        var unidade = new Location { Name = "UBS Sem Endereço", Ativo = true };
        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        await Montar(db, GeocodificadorFalso.Encontra()).GeocodificarAsync(unidade);
        await db.SaveChangesAsync();

        Assert.Empty(db.GeocodingCache);
        Assert.Equal(StatusGeocodificacao.Sucesso, unidade.StatusGeocodificacao);
    }

    [Fact]
    public async Task Cache_de_nao_encontrado_tambem_evita_reconsulta()
    {
        using var db = TestSupport.NovoContexto();
        var primeira = TestSupport.Unidade();
        var segunda = TestSupport.Unidade();
        db.Locations.AddRange(primeira, segunda);
        await db.SaveChangesAsync();

        var provedor = GeocodificadorFalso.NaoEncontra();
        var geocoder = Montar(db, provedor);

        await geocoder.GeocodificarAsync(primeira);
        await db.SaveChangesAsync();
        var r2 = await geocoder.GeocodificarAsync(segunda);

        Assert.True(r2.VeioDoCache);
        Assert.Equal(1, provedor.Chamadas);
    }

    [Fact]
    public async Task Unidade_ja_geocodificada_nao_consulta_de_novo()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        unidade.Latitude = -15.79;
        unidade.Longitude = -47.88;
        unidade.StatusGeocodificacao = StatusGeocodificacao.Sucesso;
        unidade.OrigemCoordenadas = OrigemCoordenadas.Nominatim;
        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        var provedor = GeocodificadorFalso.Encontra(lat: 0, lon: 0);
        await Montar(db, provedor).GeocodificarAsync(unidade);

        Assert.Equal(0, provedor.Chamadas);
        Assert.Equal(-15.79, unidade.Latitude, 4);
    }

    [Fact]
    public async Task Coordenada_manual_nunca_e_sobrescrita_automaticamente()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        unidade.Latitude = -15.5;
        unidade.Longitude = -47.5;
        unidade.OrigemCoordenadas = OrigemCoordenadas.Manual;
        unidade.StatusGeocodificacao = StatusGeocodificacao.Sucesso;
        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        var provedor = GeocodificadorFalso.Encontra(lat: -1, lon: -1);
        // Mesmo forçando, a origem manual é preservada sem o consentimento explícito.
        await Montar(db, provedor).GeocodificarAsync(unidade, forcar: true);

        Assert.Equal(0, provedor.Chamadas);
        Assert.Equal(-15.5, unidade.Latitude, 4);
        Assert.Equal(OrigemCoordenadas.Manual, unidade.OrigemCoordenadas);
    }

    [Fact]
    public async Task Coordenada_manual_e_substituida_quando_o_administrador_autoriza()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        unidade.Latitude = -15.5;
        unidade.Longitude = -47.5;
        unidade.OrigemCoordenadas = OrigemCoordenadas.Manual;
        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        await Montar(db, GeocodificadorFalso.Encontra(lat: -20, lon: -40))
            .GeocodificarAsync(unidade, forcar: true, sobrescreverManual: true);

        Assert.Equal(-20, unidade.Latitude, 4);
        Assert.Equal(OrigemCoordenadas.Nominatim, unidade.OrigemCoordenadas);
    }

    [Fact]
    public async Task Unidade_sem_endereco_nem_nome_nao_consulta_o_provedor()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = new Location { Name = "", Ativo = true };
        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        var provedor = GeocodificadorFalso.Encontra();
        var resultado = await Montar(db, provedor).GeocodificarAsync(unidade);

        Assert.Equal(0, provedor.Chamadas);
        Assert.Equal(StatusGeocodificacao.NaoEncontrado, resultado.Status);
    }

    [Fact]
    public void Consulta_leva_nome_endereco_cidade_e_pais()
    {
        var consulta = UnitGeocoder.MontarConsulta(TestSupport.Unidade());

        Assert.Contains("UBS 1 Asa Norte", consulta);
        Assert.Contains("SGAN 906", consulta);
        Assert.Contains("Brasília", consulta);
        Assert.Contains("DF", consulta);
        Assert.EndsWith("Brasil", consulta);
    }
}

/// <summary>Normalização do endereço, que é a chave do cache.</summary>
public class AddressNormalizerTests
{
    private readonly AddressNormalizer _normalizer = new();

    [Theory]
    [InlineData("SGAN 906, Brasília - DF", "sgan 906 brasilia df")]
    [InlineData("SGAN 906 Brasilia DF", "sgan 906 brasilia df")]
    [InlineData("  SGAN   906,,  BRASÍLIA/df  ", "sgan 906 brasilia df")]
    public void Escritas_diferentes_do_mesmo_endereco_geram_a_mesma_chave(string entrada, string esperado)
        => Assert.Equal(esperado, _normalizer.Normalizar(entrada));

    [Fact]
    public void Numero_do_endereco_e_preservado()
    {
        // O número distingue duas unidades na mesma via: perdê-lo colidiria o cache.
        Assert.NotEqual(_normalizer.Normalizar("Quadra 10 Lote 5"),
                        _normalizer.Normalizar("Quadra 10 Lote 7"));
    }

    [Theory]
    [InlineData("70790-060", "70790-060")]
    [InlineData("70790060", "70790-060")]
    [InlineData("70.790-060", "70790-060")]
    public void Cep_valido_e_padronizado(string entrada, string esperado)
        => Assert.Equal(esperado, _normalizer.NormalizarCep(entrada));

    [Theory]
    [InlineData("123")]
    [InlineData("")]
    [InlineData(null)]
    public void Cep_invalido_devolve_nulo(string? entrada)
        => Assert.Null(_normalizer.NormalizarCep(entrada));

    [Theory]
    [InlineData("df", "DF")]
    [InlineData(" Df ", "DF")]
    public void Uf_e_padronizada(string entrada, string esperado)
        => Assert.Equal(esperado, _normalizer.NormalizarUf(entrada));

    [Theory]
    [InlineData("Distrito Federal")]
    [InlineData("D")]
    public void Uf_invalida_devolve_nulo(string entrada)
        => Assert.Null(_normalizer.NormalizarUf(entrada));
}
