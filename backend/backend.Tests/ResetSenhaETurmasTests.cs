using EstagioCheck.API.Controllers;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EstagioCheck.API.Tests;

public class ResetSenhaTests
{
    [Fact]
    public async Task Senha_do_aluno_volta_para_o_rgm_e_exige_troca()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno("Aluno", "77676035");
        aluno.PasswordHash = BCrypt.Net.BCrypt.HashPassword("outra-senha");
        aluno.MustChangePassword = false;
        db.Add(aluno);
        await db.SaveChangesAsync();

        var r = await new UsersController(db, new ConflitoTurmasService(db)).ResetarSenha(aluno.Id);

        Assert.IsType<OkObjectResult>(r);
        Assert.True(BCrypt.Net.BCrypt.Verify("77676035", aluno.PasswordHash));
        Assert.True(aluno.MustChangePassword);
    }

    [Fact]
    public async Task Perfil_que_nao_e_aluno_nao_tem_senha_padrao()
    {
        using var db = TestSupport.NovoContexto();
        var preceptor = TestSupport.Usuario(Roles.Preceptor);
        db.Add(preceptor);
        await db.SaveChangesAsync();

        var r = await new UsersController(db, new ConflitoTurmasService(db)).ResetarSenha(preceptor.Id);

        Assert.IsType<BadRequestObjectResult>(r);
    }
}

public class TurmasVigentesTests
{
    private static GroupMembership Vinculo(string codigo, params DateOnly[] fins) => new()
    {
        Group = new StudentGroup
        {
            Code = codigo, Name = codigo,
            Schedules = [.. fins.Select(f => new RotationSchedule { StartDate = f.AddDays(-30), EndDate = f })]
        }
    };

    private static readonly DateOnly Hoje = new(2026, 9, 22);

    [Fact]
    public void Turma_com_rodizios_encerrados_some_do_cabecalho()
    {
        var vigentes = TurmasDoAluno.Vigentes(
            [Vinculo("ES1-M05", Hoje.AddDays(30)), Vinculo("PIC-T01", Hoje.AddDays(-5)), Vinculo("NOVA")], Hoje);

        Assert.Equal(["ES1-M05", "NOVA"], vigentes.Select(v => v.Group.Code).Order());
    }

    [Fact]
    public void Sem_nenhuma_vigente_mostra_todas()
    {
        var vigentes = TurmasDoAluno.Vigentes([Vinculo("ANTIGA", Hoje.AddDays(-60))], Hoje);

        Assert.Single(vigentes);
    }
}
