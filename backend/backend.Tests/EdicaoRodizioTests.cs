using EstagioCheck.API.Controllers;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>Salvar a edição duas vezes seguidas não pode acusar edição concorrente.</summary>
public class EdicaoRodizioTests
{
    private sealed record Cenario(AppDbContext Db, StudentGroup Turma, Location Ubs, ApplicationUser Preceptor, Location Udf);

    private static GroupsController Montar(AppDbContext db) => new(db, new ConflitoTurmasService(db));

    private static async Task<Cenario> MontarCenarioAsync()
    {
        var db = TestSupport.NovoContexto();
        var turma = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var ubs = TestSupport.Unidade("UBS Sobradinho");
        var preceptor = TestSupport.Usuario(Roles.Preceptor, "Preceptor");
        var aluno = TestSupport.Aluno();
        var udf = TestSupport.Unidade("UDF Centro Universitário — Campus Sede (Laboratórios de Enfermagem)");
        udf.IsInstitution = true;

        db.AddRange(turma, ubs, preceptor, aluno, udf);
        db.Add(new GroupMembership { StudentId = aluno.Id, GroupId = turma.Id });
        await db.SaveChangesAsync();

        return new Cenario(db, turma, ubs, preceptor, udf);
    }

    private static CreateScheduleDto Dto(Cenario c, params int[] dias) => new(
        c.Turma.Id, c.Ubs.Id, c.Preceptor.Id, Turnos.Manha, "07/09 a 18/09",
        new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 18), "assistencia", 80, null,
        [.. dias.Select(d => new CriarDiaRodizioDto(d, ModoAtividade.Presencial, null, null))]);

    private static ScheduleDto Corpo(ActionResult<ScheduleDto> resposta) =>
        (ScheduleDto)((ObjectResult)resposta.Result!).Value!;

    [Fact]
    public async Task Salvar_a_mesma_edicao_duas_vezes_nao_acusa_edicao_concorrente()
    {
        var c = await MontarCenarioAsync();
        using var db = c.Db;
        var controller = Montar(db);

        var criada = Corpo(await controller.CreateSchedule(Dto(c, 1, 2, 3, 4, 5)));
        var edicao = Dto(c, 1, 2, 4, 5);

        // O clique duplo no botão manda a mesma edição duas vezes.
        var primeira = Corpo(await controller.UpdateSchedule(criada.Id, edicao));
        var segunda = Corpo(await controller.UpdateSchedule(criada.Id, edicao));

        Assert.Equal([1, 2, 4, 5], primeira.Days.Select(d => d.DayOfWeek));
        Assert.Equal([1, 2, 4, 5], segunda.Days.Select(d => d.DayOfWeek));
    }

    [Fact]
    public async Task Edicao_altera_o_dia_que_permanece_em_vez_de_recria_lo()
    {
        var c = await MontarCenarioAsync();
        using var db = c.Db;
        var controller = Montar(db);

        var criada = Corpo(await controller.CreateSchedule(Dto(c, 1, 2)));
        var idDaSegunda = await db.RotationDaySchedules
            .Where(d => d.ScheduleId == criada.Id && d.DayOfWeek == 2)
            .Select(d => d.Id)
            .SingleAsync();

        await controller.UpdateSchedule(criada.Id, new CreateScheduleDto(
            c.Turma.Id, c.Ubs.Id, c.Preceptor.Id, Turnos.Manha, "07/09 a 18/09",
            new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 18), "assistencia", 80, null,
            [
                new CriarDiaRodizioDto(1, ModoAtividade.Presencial, null, null),
                new CriarDiaRodizioDto(2, ModoAtividade.Remoto, null, "Aula na faculdade")
            ]));

        var terca = await db.RotationDaySchedules.SingleAsync(d => d.ScheduleId == criada.Id && d.DayOfWeek == 2);
        Assert.Equal(idDaSegunda, terca.Id);
        Assert.Equal(ModoAtividade.Remoto, terca.Mode);
        Assert.Equal("Aula na faculdade", terca.Notes);
    }

    [Fact]
    public async Task Programacao_vazia_devolve_o_rodizio_ao_padrao()
    {
        var c = await MontarCenarioAsync();
        using var db = c.Db;
        var controller = Montar(db);

        var criada = Corpo(await controller.CreateSchedule(Dto(c, 1, 2, 3)));
        var atualizada = Corpo(await controller.UpdateSchedule(criada.Id, Dto(c)));

        Assert.Empty(atualizada.Days);
        Assert.Empty(await db.RotationDaySchedules.Where(d => d.ScheduleId == criada.Id).ToListAsync());
    }

    [Fact]
    public async Task Sexta_presencial_e_sempre_na_udf()
    {
        var c = await MontarCenarioAsync();
        using var db = c.Db;

        var criada = Corpo(await Montar(db).CreateSchedule(new CreateScheduleDto(
            c.Turma.Id, c.Ubs.Id, c.Preceptor.Id, Turnos.Manha, "07/09 a 18/09",
            new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 18), "assistencia", 80, null,
            [
                new CriarDiaRodizioDto(4, ModoAtividade.Presencial, c.Ubs.Id, null),
                new CriarDiaRodizioDto(DiasSemana.Sexta, ModoAtividade.Presencial, c.Ubs.Id, null)
            ])));

        Assert.Equal(c.Ubs.Id, criada.Days.Single(d => d.DayOfWeek == 4).LocationId);
        Assert.Equal(c.Udf.Id, criada.Days.Single(d => d.DayOfWeek == DiasSemana.Sexta).LocationId);
    }

    [Fact]
    public async Task Sexta_remota_nao_ganha_local()
    {
        var c = await MontarCenarioAsync();
        using var db = c.Db;

        var criada = Corpo(await Montar(db).CreateSchedule(new CreateScheduleDto(
            c.Turma.Id, c.Ubs.Id, c.Preceptor.Id, Turnos.Manha, "07/09 a 18/09",
            new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 18), "assistencia", 80, null,
            [new CriarDiaRodizioDto(DiasSemana.Sexta, ModoAtividade.Remoto, c.Ubs.Id, null)])));

        Assert.Null(criada.Days.Single().LocationId);
    }
}
