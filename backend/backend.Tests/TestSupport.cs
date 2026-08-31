using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services.Geocoding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstagioCheck.API.Tests;

/// <summary>Apoio comum aos testes: banco em memória e dublês do geocodificador.</summary>
public static class TestSupport
{
    /// <summary>Contexto isolado por teste, para um não enxergar os dados do outro.</summary>
    public static AppDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"estagiocheck_{Guid.NewGuid()}")
            // O provedor em memória não aplica os filtros de índice do PostgreSQL;
            // os testes verificam a regra na camada de serviço/controller.
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    public static ILogger<T> Logger<T>() => NullLogger<T>.Instance;

    public static ApplicationUser Aluno(string nome = "Aluno Teste", string? rgm = "12345678") => new()
    {
        FullName = nome,
        Email = $"{Guid.NewGuid():N}@cs.udf.edu.br",
        Role = Roles.Aluno,
        Rgm = rgm,
        Semester = 7,
        Shift = "manha",
        IsActive = true
    };

    public static ApplicationUser Usuario(string papel, string nome = "Fulano") => new()
    {
        FullName = nome,
        Email = $"{Guid.NewGuid():N}@cs.udf.edu.br",
        Role = papel,
        IsActive = true
    };

    public static Location Unidade(string nome = "UBS 1 Asa Norte") => new()
    {
        Name = nome,
        Address = "SGAN 906",
        Numero = "S/N",
        Bairro = "Asa Norte",
        Cidade = "Brasília",
        Uf = "DF",
        Cep = "70790-060",
        Ativo = true,
        StatusGeocodificacao = StatusGeocodificacao.Pendente
    };
}

/// <summary>Geocodificador programável, para exercitar cada desfecho possível.</summary>
public class GeocodificadorFalso : IGeocodingService
{
    private readonly Func<string, GeocodingResult?> _resposta;

    public GeocodificadorFalso(Func<string, GeocodingResult?> resposta) => _resposta = resposta;

    /// <summary>Quantas vezes o provedor foi realmente consultado — prova do cache.</summary>
    public int Chamadas { get; private set; }

    public string Provedor => OrigemCoordenadas.Nominatim;

    public Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        Chamadas++;
        return Task.FromResult(_resposta(address));
    }

    public static GeocodificadorFalso Encontra(
        double lat = -15.7401, double lon = -47.8829, bool duvidoso = false) =>
        new(_ => new GeocodingResult(lat, lon, "SGAN 906, Brasília - DF, Brasil",
            duvidoso ? "city" : "building", duvidoso,
            duvidoso ? "Resultado pouco específico (city)." : null));

    public static GeocodificadorFalso NaoEncontra() => new(_ => null);

    public static GeocodificadorFalso Falha(bool limiteExcedido = false) =>
        new(_ => throw new GeocodingException(
            limiteExcedido ? "429" : "timeout", limiteExcedido));
}
