using EstagioCheck.API.Controllers;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>Janela da contagem: do rodízio e da entrada do aluno na turma até ontem, mais o sábado programado.</summary>
public class PendenciasTests
{
    private static DateOnly Hoje => BrasiliaTime.Hoje;

    private sealed record Cenario(AppDbContext Db, ApplicationUser Aluno, RotationSchedule Escala);

    /// <summary>Aluno que entrou na turma há <paramref name="diasNaTurma"/> dias.</summary>
    private static async Task<Cenario> MontarAsync(
        DateOnly inicioEscala, DateOnly fimEscala, int diasNaTurma = 60, bool comVinculo = true)
    {
        var db = TestSupport.NovoContexto();

        var aluno = TestSupport.Aluno();
        var grupo = new StudentGroup { Code = "T02", Name = "Teste" };
        var ubs = TestSupport.Unidade();
        var escala = new RotationSchedule
        {
            GroupId = grupo.Id,
            LocationId = ubs.Id,
            Shift = Turnos.Manha,
            PeriodLabel = "Rodízio",
            StartDate = inicioEscala,
            EndDate = fimEscala,
            ActivityType = "assistencia",
            RequiredHours = 80
        };

        db.AddRange(aluno, grupo, ubs, escala);
        if (comVinculo)
            db.Add(new GroupMembership
            {
                StudentId = aluno.Id,
                GroupId = grupo.Id,
                CreatedAt = BrasiliaTime.Agora.AddDays(-diasNaTurma)
            });
        await db.SaveChangesAsync();

        return new Cenario(db, aluno, escala);
    }

    private static PendenciasService Servico(AppDbContext db) => new(db, new ProgramacaoService(db));

    [Fact]
    public async Task Rodizio_com_data_de_inicio_invalida_nao_gera_pendencias()
    {
        // Unix epoch: a data que produziu os milhares de dias no painel.
        var c = await MontarAsync(new DateOnly(1970, 1, 1), Hoje.AddDays(30));
        using var _ = c.Db;

        var pendencias = await Servico(c.Db).CalcularAsync(c.Aluno.Id);

        Assert.Empty(pendencias);
    }

    [Fact]
    public async Task Rodizio_com_data_vazia_nao_gera_pendencias()
    {
        var c = await MontarAsync(default, Hoje.AddDays(30));
        using var _ = c.Db;

        Assert.Empty(await Servico(c.Db).CalcularAsync(c.Aluno.Id));
    }

    [Fact]
    public async Task Sem_vinculo_a_turma_nao_ha_pendencias()
    {
        var c = await MontarAsync(Hoje.AddDays(-30), Hoje.AddDays(30), comVinculo: false);
        using var _ = c.Db;

        Assert.Empty(await Servico(c.Db).CalcularAsync(c.Aluno.Id));
    }

    [Fact]
    public async Task Pendencias_comecam_na_entrada_do_aluno_na_turma()
    {
        // Rodízio começou há 60 dias, mas o aluno só entrou na turma há 7.
        var c = await MontarAsync(Hoje.AddDays(-60), Hoje.AddDays(30), diasNaTurma: 7);
        using var _ = c.Db;

        var pendencias = await Servico(c.Db).CalcularAsync(c.Aluno.Id);

        var entrada = Hoje.AddDays(-7);
        Assert.All(pendencias, p => Assert.True(p.PendencyDate >= entrada));
        Assert.All(pendencias, p => Assert.True(p.PendencyDate < Hoje));
        // Sete dias corridos têm sempre cinco dias úteis.
        Assert.Equal(5, pendencias.Count);
    }

    [Fact]
    public async Task Hoje_e_datas_futuras_nao_contam()
    {
        var c = await MontarAsync(Hoje, Hoje.AddDays(30), diasNaTurma: 60);
        using var _ = c.Db;

        Assert.Empty(await Servico(c.Db).CalcularAsync(c.Aluno.Id));
    }

    [Fact]
    public async Task Sabado_programado_conta_como_dia_de_estagio()
    {
        var c = await MontarAsync(Hoje.AddDays(-60), Hoje.AddDays(30), diasNaTurma: 7);
        using var _ = c.Db;

        c.Db.RotationDaySchedules.Add(new RotationDaySchedule
        {
            ScheduleId = c.Escala.Id,
            DayOfWeek = (int)DayOfWeek.Saturday,
            Mode = ModoAtividade.Presencial
        });
        await c.Db.SaveChangesAsync();

        var pendencias = await Servico(c.Db).CalcularAsync(c.Aluno.Id);

        Assert.Contains(pendencias, p => p.PendencyDate.DayOfWeek == DayOfWeek.Saturday);
        Assert.DoesNotContain(pendencias, p => p.PendencyDate.DayOfWeek == DayOfWeek.Sunday);
        Assert.Equal(6, pendencias.Count);
    }
}

public class RodizioValidacaoTests
{
    private static async Task<(AppDbContext Db, CreateScheduleDto Dto)> MontarAsync(
        DateOnly inicio, DateOnly fim)
    {
        var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        var preceptor = TestSupport.Usuario(Roles.Preceptor, "Preceptora");
        var grupo = new StudentGroup { Code = "T02", Name = "Teste" };
        var ubs = TestSupport.Unidade();

        db.AddRange(aluno, preceptor, grupo, ubs);
        db.Add(new GroupMembership { StudentId = aluno.Id, GroupId = grupo.Id });
        await db.SaveChangesAsync();

        var dto = new CreateScheduleDto(grupo.Id, ubs.Id, preceptor.Id, "manha", "Rodízio",
            inicio, fim, "assistencia", 80, null);
        return (db, dto);
    }

    private static string? Campo(ObjectResult r) =>
        r.Value?.GetType().GetProperty("field")?.GetValue(r.Value) as string;

    [Fact]
    public async Task Data_de_inicio_vazia_e_recusada_apontando_o_campo()
    {
        var (db, dto) = await MontarAsync(default, new DateOnly(2026, 10, 30));
        using var _ = db;

        var r = await new GroupsController(db, new ConflitoTurmasService(db)).CreateSchedule(dto);

        var falha = Assert.IsAssignableFrom<ObjectResult>(r.Result);
        Assert.Equal(400, falha.StatusCode);
        Assert.Equal("startDate", Campo(falha));
        Assert.Empty(db.RotationSchedules);
    }

    [Fact]
    public async Task Ano_fora_do_intervalo_e_recusado()
    {
        var (db, dto) = await MontarAsync(new DateOnly(1970, 1, 1), new DateOnly(2026, 10, 30));
        using var _ = db;

        var falha = Assert.IsAssignableFrom<ObjectResult>((await new GroupsController(db, new ConflitoTurmasService(db)).CreateSchedule(dto)).Result);
        Assert.Equal(400, falha.StatusCode);
        Assert.Equal("startDate", Campo(falha));
    }

    [Fact]
    public async Task Periodo_maior_que_um_ano_e_recusado()
    {
        var (db, dto) = await MontarAsync(new DateOnly(2026, 1, 1), new DateOnly(2027, 6, 30));
        using var _ = db;

        var falha = Assert.IsAssignableFrom<ObjectResult>((await new GroupsController(db, new ConflitoTurmasService(db)).CreateSchedule(dto)).Result);
        Assert.Equal("endDate", Campo(falha));
    }

    [Fact]
    public async Task Sabado_na_programacao_semanal_e_aceito()
    {
        var (db, dto) = await MontarAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 30));
        using var _ = db;

        var comSabado = dto with
        {
            Days = [new CriarDiaRodizioDto((int)DayOfWeek.Saturday, ModoAtividade.Presencial, null, null)]
        };

        var r = await new GroupsController(db, new ConflitoTurmasService(db)).CreateSchedule(comSabado);

        var ok = Assert.IsType<OkObjectResult>(r.Result);
        var escala = Assert.IsType<ScheduleDto>(ok.Value);
        Assert.Contains(escala.Days, d => d.DayOfWeek == 6 && d.DayLabel == "Sábado");
    }

    [Theory]
    [InlineData("$.startDate", "startDate")]
    [InlineData("Days[0].Mode", "days[0].mode")]
    [InlineData("TaskInstructions", "taskInstructions")]
    public void Nome_do_campo_segue_o_formulario_da_tela(string chave, string esperado) =>
        Assert.Equal(esperado, ErrosApi.NomeCampo(chave));
}

/// <summary>Conclusão individual do aluno (botão de capelo) e sua reversão.</summary>
public class ConclusaoAlunoTests
{
    [Fact]
    public async Task Concluir_move_para_inativos_e_reativar_desfaz()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        db.Add(aluno);
        await db.SaveChangesAsync();

        var controller = new UsersController(db, new ConflitoTurmasService(db));

        var concluido = Assert.IsType<OkObjectResult>((await controller.Concluir(aluno.Id)).Result);
        Assert.False(Assert.IsType<UserDto>(concluido.Value).IsActive);
        Assert.Single(db.StudentSemesterHistories);

        // Segunda conclusão do mesmo aluno é recusada, sem duplicar o histórico.
        Assert.IsType<ConflictObjectResult>((await controller.Concluir(aluno.Id)).Result);
        Assert.Single(db.StudentSemesterHistories);

        var reativado = Assert.IsType<OkObjectResult>((await controller.Reativar(aluno.Id)).Result);
        Assert.True(Assert.IsType<UserDto>(reativado.Value).IsActive);
        // O histórico fica como trilha do que aconteceu.
        Assert.Single(db.StudentSemesterHistories);
    }

    [Fact]
    public async Task Somente_aluno_pode_ser_concluido()
    {
        using var db = TestSupport.NovoContexto();
        var preceptor = TestSupport.Usuario(Roles.Preceptor);
        db.Add(preceptor);
        await db.SaveChangesAsync();

        var r = await new UsersController(db, new ConflitoTurmasService(db)).Concluir(preceptor.Id);

        Assert.IsType<BadRequestObjectResult>(r.Result);
        Assert.True(db.Users.Single().IsActive);
    }
}
