using EstagioCheck.API.Controllers;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>
/// O ponto só é aceito dentro do raio de uma unidade com localização confirmada.
/// Cada teste fecha uma brecha encontrada: ponto sem unidade passava sem checagem
/// de distância; unidade sem coordenadas recusava o aluno com "você está a
/// 5.000 km"; e a precisão informada pelo celular, sem limite, ampliava o raio.
/// </summary>
public class RaioCheckInTests
{
    private const double Lat = -15.7401, Lon = -47.8829;

    private static AttendanceController Montar(AppDbContext db, Guid alunoId)
    {
        var controller = new AttendanceController(db, new GeoService(), new ProgramacaoService(db));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, alunoId.ToString()),
                    new Claim(ClaimTypes.Role, Roles.Aluno)
                ], "teste"))
            }
        };
        return controller;
    }

    private static async Task<(AppDbContext db, ApplicationUser aluno, Location unidade)> MontarAsync(
        Action<Location>? ajustar = null)
    {
        var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        var unidade = TestSupport.Unidade();
        unidade.Latitude = Lat;
        unidade.Longitude = Lon;
        unidade.RadiusMeters = 100;
        unidade.StatusGeocodificacao = StatusGeocodificacao.Sucesso;
        ajustar?.Invoke(unidade);
        db.AddRange(aluno, unidade);
        await db.SaveChangesAsync();
        return (db, aluno, unidade);
    }

    private static string? Codigo(IActionResult? resultado) =>
        (resultado as ObjectResult)?.Value?.GetType().GetProperty("code")?.GetValue(((ObjectResult)resultado).Value) as string;

    /// <summary>Ponto a <paramref name="metrosAoNorte"/> metros da unidade.</summary>
    private static CreateAttendanceDto Ponto(Guid? unidadeId, double metrosAoNorte = 0, double? precisao = null) =>
        new(Lat + metrosAoNorte / 111_320d, Lon, "check_in", null, unidadeId, null, null, precisao);

    [Fact]
    public async Task Ponto_sem_unidade_e_recusado()
    {
        var (db, aluno, _) = await MontarAsync();
        using var _db = db;

        var r = await Montar(db, aluno.Id).Create(Ponto(unidadeId: null));

        Assert.IsType<BadRequestObjectResult>(r.Result);
        Assert.Equal("sem_unidade", Codigo(r.Result));
        Assert.Empty(db.AttendanceRecords);
    }

    [Theory]
    [InlineData(StatusGeocodificacao.NaoEncontrado)]
    [InlineData(StatusGeocodificacao.RevisaoManual)]
    [InlineData(StatusGeocodificacao.Pendente)]
    public async Task Unidade_sem_localizacao_confirmada_recusa_com_motivo_proprio(string status)
    {
        // Mesmo com coordenadas gravadas: status duvidoso não serve para o raio.
        var (db, aluno, unidade) = await MontarAsync(u => u.StatusGeocodificacao = status);
        using var _db = db;

        var r = await Montar(db, aluno.Id).Create(Ponto(unidade.Id));

        Assert.Equal("unidade_sem_localizacao", Codigo(r.Result));
        Assert.Empty(db.AttendanceRecords);
    }

    [Fact]
    public async Task Unidade_com_coordenadas_zeradas_nao_culpa_o_aluno_pela_distancia()
    {
        var (db, aluno, unidade) = await MontarAsync(u => { u.Latitude = 0; u.Longitude = 0; });
        using var _db = db;

        var r = await Montar(db, aluno.Id).Create(Ponto(unidade.Id));

        // Antes: "fora_do_raio", "você está a 5.000 km".
        Assert.Equal("unidade_sem_localizacao", Codigo(r.Result));
    }

    [Fact]
    public async Task Dentro_do_raio_o_ponto_e_registrado()
    {
        var (db, aluno, unidade) = await MontarAsync();
        using var _db = db;

        var r = await Montar(db, aluno.Id).Create(Ponto(unidade.Id, metrosAoNorte: 60));

        Assert.IsNotType<BadRequestObjectResult>(r.Result);
        Assert.Single(db.AttendanceRecords);
    }

    [Fact]
    public async Task Precisao_informada_pelo_celular_nao_amplia_o_raio_sem_limite()
    {
        var (db, aluno, unidade) = await MontarAsync();
        using var _db = db;

        // A 2 km da unidade, dizendo ter precisão de 10 km: antes passava.
        var r = await Montar(db, aluno.Id).Create(Ponto(unidade.Id, metrosAoNorte: 2000, precisao: 10_000));

        Assert.Equal("fora_do_raio", Codigo(r.Result));
        Assert.Empty(db.AttendanceRecords);
    }

    [Fact]
    public async Task Imprecisao_razoavel_do_gps_ainda_e_tolerada()
    {
        var (db, aluno, unidade) = await MontarAsync();
        using var _db = db;

        // 130 m de uma unidade de raio 100, com GPS de ±40 m: dentro da margem.
        var r = await Montar(db, aluno.Id).Create(Ponto(unidade.Id, metrosAoNorte: 130, precisao: 40));

        Assert.IsNotType<BadRequestObjectResult>(r.Result);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-30.0, 0)]
    [InlineData(20.0, 20)]
    [InlineData(10_000.0, AttendanceController.ToleranciaGpsMaximaMetros)]
    public void Tolerancia_do_gps_fica_entre_zero_e_o_teto(double? informada, double esperada) =>
        Assert.Equal(esperada, AttendanceController.ToleranciaGps(informada));
}

/// <summary>Regras de coordenada e de código CNES comuns a todo caminho de cadastro.</summary>
public class CadastroUnidadeRegrasTests
{
    [Theory]
    [InlineData(0, 0, "(0, 0)")]
    [InlineData(-95, -47.88, "Latitude")]
    [InlineData(-15.74, 190, "Longitude")]
    public void Coordenada_invalida_e_recusada(double lat, double lon, string trecho) =>
        Assert.Contains(trecho, Coordenadas.Validar(lat, lon));

    [Fact]
    public void Coordenada_de_brasilia_e_aceita() =>
        Assert.Null(Coordenadas.Validar(-15.7401, -47.8829));

    [Theory]
    [InlineData("10731", "0010731")]
    [InlineData("0010731", "0010731")]
    [InlineData(" 2650355 ", "2650355")]
    public void Cnes_e_padronizado_em_sete_digitos(string entrada, string esperado) =>
        Assert.Equal(esperado, Cnes.Normalizar(entrada));

    [Theory]
    [InlineData("")]
    [InlineData("12345678")]
    [InlineData("12A45")]
    public void Cnes_invalido_vira_nulo(string entrada) =>
        Assert.Null(Cnes.Normalizar(entrada));

    [Fact]
    public void Cnes_da_api_numerica_ganha_os_zeros() =>
        Assert.Equal("0010456", Cnes.Formatar(10456));
}
