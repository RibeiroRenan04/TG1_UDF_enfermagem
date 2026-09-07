using EstagioCheck.API.Controllers;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>
/// Trava de duplicidade das irregularidades, definida na reunião de 06/09/2026:
/// um ponto tem uma contestação por vez. Enquanto a atual não for NEGADA pelo
/// professor, o aluno não abre outra para o mesmo ponto.
///
/// A ocorrência também carrega a data/hora exata do ponto contestado, que o
/// painel exibe ao lado da data de abertura.
/// </summary>
public class IrregularidadeTests
{
    private static IrregularitiesController Montar(
        EstagioCheck.API.Data.AppDbContext db, Guid usuarioId, string papel = Roles.Aluno)
    {
        var controller = new IrregularitiesController(db);
        var identidade = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
            new Claim(ClaimTypes.Role, papel)
        ], "teste");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new ClaimsPrincipal(identidade)
            }
        };
        return controller;
    }

    private static T Corpo<T>(ActionResult<T> resultado) where T : class =>
        (T)((ObjectResult)resultado.Result!).Value!;

    private static CreateIrregularityDto Ocorrencia(Guid? pontoId, string descricao = "Cheguei atrasado por causa do transporte.") =>
        new("atraso", BrasiliaTime.Hoje, descricao, pontoId, null);

    /// <summary>Aluno com um registro de ponto para contestar.</summary>
    private static async Task<(EstagioCheck.API.Data.AppDbContext db, ApplicationUser aluno, AttendanceRecord ponto)>
        ComPontoAsync()
    {
        var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var ponto = new AttendanceRecord
        {
            StudentId = aluno.Id,
            LocationId = unidade.Id,
            Type = "check_in",
            RecordedAt = BrasiliaTime.Agora,
            Status = "irregular"
        };
        db.AttendanceRecords.Add(ponto);
        await db.SaveChangesAsync();

        return (db, aluno, ponto);
    }

    // ── Trava de duplicidade ──────────────────────────────────────────────────
    [Fact]
    public async Task Segunda_ocorrencia_para_o_mesmo_ponto_e_recusada()
    {
        var (db, aluno, ponto) = await ComPontoAsync();
        using var _ = db;

        var controller = Montar(db, aluno.Id);
        await controller.Create(Ocorrencia(ponto.Id));

        var resposta = await controller.Create(Ocorrencia(ponto.Id, "Tentando de novo pelo mesmo ponto."));

        Assert.IsType<ConflictObjectResult>(resposta.Result);
        Assert.Single(db.PointIrregularities);
    }

    [Fact]
    public async Task Ocorrencia_negada_libera_uma_nova_contestacao_do_mesmo_ponto()
    {
        var (db, aluno, ponto) = await ComPontoAsync();
        using var _ = db;
        var professor = TestSupport.Usuario(Roles.Supervisor, "Professora");
        db.Add(professor);
        await db.SaveChangesAsync();

        var primeira = Corpo(await Montar(db, aluno.Id).Create(Ocorrencia(ponto.Id)));

        // O professor nega: é o que a reunião definiu como gatilho para reabrir.
        await Montar(db, professor.Id, Roles.Supervisor)
            .ProfessorDecision(primeira.Id, new ProfessorDecisionIrregularityDto(false, "Sem comprovação."));

        var resposta = await Montar(db, aluno.Id)
            .Create(Ocorrencia(ponto.Id, "Agora com o comprovante do transporte."));

        Assert.IsType<OkObjectResult>(resposta.Result);
        Assert.Equal(2, db.PointIrregularities.Count());
    }

    [Fact]
    public async Task Ocorrencia_aprovada_mantem_o_ponto_bloqueado()
    {
        var (db, aluno, ponto) = await ComPontoAsync();
        using var _ = db;
        var professor = TestSupport.Usuario(Roles.Supervisor, "Professora");
        db.Add(professor);
        await db.SaveChangesAsync();

        var primeira = Corpo(await Montar(db, aluno.Id).Create(Ocorrencia(ponto.Id)));

        await Montar(db, professor.Id, Roles.Supervisor)
            .ProfessorDecision(primeira.Id, new ProfessorDecisionIrregularityDto(true, "Justificativa aceita."));

        // Aprovada, o ponto já foi regularizado: não há o que contestar de novo.
        var resposta = await Montar(db, aluno.Id).Create(Ocorrencia(ponto.Id));

        Assert.IsType<ConflictObjectResult>(resposta.Result);
    }

    [Fact]
    public async Task Pontos_diferentes_aceitam_ocorrencias_simultaneas()
    {
        var (db, aluno, ponto) = await ComPontoAsync();
        using var _ = db;

        var outroPonto = new AttendanceRecord
        {
            StudentId = aluno.Id,
            Type = "check_out",
            RecordedAt = BrasiliaTime.Agora,
            Status = "pendente"
        };
        db.AttendanceRecords.Add(outroPonto);
        await db.SaveChangesAsync();

        var controller = Montar(db, aluno.Id);
        await controller.Create(Ocorrencia(ponto.Id));

        var resposta = await controller.Create(Ocorrencia(outroPonto.Id, "Também esqueci a saída."));

        Assert.IsType<OkObjectResult>(resposta.Result);
        Assert.Equal(2, db.PointIrregularities.Count());
    }

    [Fact]
    public async Task Ocorrencia_sem_ponto_trava_por_tipo_e_data()
    {
        var (db, aluno, _) = await ComPontoAsync();
        using var contexto = db;

        var controller = Montar(db, aluno.Id);
        await controller.Create(Ocorrencia(null));

        var resposta = await controller.Create(Ocorrencia(null, "Mesma data, mesmo tipo, outra descrição."));

        Assert.IsType<ConflictObjectResult>(resposta.Result);
        Assert.Single(db.PointIrregularities);
    }

    // ── Dados do ponto contestado ─────────────────────────────────────────────
    [Fact]
    public async Task Ocorrencia_carrega_a_data_hora_exata_do_ponto()
    {
        var (db, aluno, ponto) = await ComPontoAsync();
        using var _ = db;

        var criada = Corpo(await Montar(db, aluno.Id).Create(Ocorrencia(ponto.Id)));

        // O painel exibe esta data ao lado da data de abertura da ocorrência.
        Assert.Equal(ponto.RecordedAt, criada.AttendanceRecordedAt);
        Assert.Equal("check_in", criada.AttendanceType);
        Assert.Equal("irregular", criada.AttendanceStatus);
        Assert.NotNull(criada.AttendanceLocationName);
    }

    [Fact]
    public async Task Aluno_ve_a_ocorrencia_recem_registrada_na_propria_listagem()
    {
        var (db, aluno, ponto) = await ComPontoAsync();
        using var _ = db;

        var controller = Montar(db, aluno.Id);
        await controller.Create(Ocorrencia(ponto.Id));

        var lista = ((IEnumerable<IrregularityDto>)((ObjectResult)(await controller.GetAll(null, null)).Result!).Value!)
            .ToList();

        Assert.Single(lista);
        Assert.Equal(PointIrregularity.StatusAguardandoPreceptor, lista[0].Status);
        Assert.Equal(ponto.RecordedAt, lista[0].AttendanceRecordedAt);
    }

    [Fact]
    public async Task Ponto_de_outro_aluno_nao_pode_ser_contestado()
    {
        var (db, aluno, ponto) = await ComPontoAsync();
        using var _ = db;
        var colega = TestSupport.Aluno("Colega", "87654321");
        db.Add(colega);
        await db.SaveChangesAsync();

        var resposta = await Montar(db, colega.Id).Create(Ocorrencia(ponto.Id));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
        Assert.Empty(db.PointIrregularities);
    }
}
