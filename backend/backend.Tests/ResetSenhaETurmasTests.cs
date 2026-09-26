using EstagioCheck.API.Controllers;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using EstagioCheck.API.Services.Seguranca;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text.Json;
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

        var r = await new UsersController(db, new ConflitoTurmasService(db), TestSupport.Protecao()).ResetarSenha(aluno.Id);

        Assert.IsType<OkObjectResult>(r);
        Assert.True(BCrypt.Net.BCrypt.Verify("77676035", aluno.PasswordHash));
        Assert.True(aluno.MustChangePassword);
    }

    [Theory]
    [InlineData(Roles.Preceptor)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Secretaria)]
    public async Task Equipe_recebe_senha_provisoria_e_troca_obrigatoria(string papel)
    {
        using var db = TestSupport.NovoContexto();
        var membro = TestSupport.Usuario(papel);
        membro.PasswordHash = BCrypt.Net.BCrypt.HashPassword("esquecida1", workFactor: 4);
        db.Add(membro);
        await db.SaveChangesAsync();

        var r = await new UsersController(db, new ConflitoTurmasService(db), TestSupport.Protecao()).ResetarSenha(membro.Id);

        var corpo = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(r).Value)).RootElement;
        var provisoria = corpo.GetProperty("senhaProvisoria").GetString()!;
        Assert.Equal(SenhaProvisoria.Tamanho, provisoria.Length);
        Assert.Null(PoliticaSenha.Validar(provisoria));
        Assert.True(BCrypt.Net.BCrypt.Verify(provisoria, membro.PasswordHash));
        Assert.True(membro.MustChangePassword);
    }

    [Fact]
    public void Senhas_provisorias_nao_se_repetem()
    {
        var geradas = Enumerable.Range(0, 200).Select(_ => SenhaProvisoria.Gerar()).ToHashSet();
        Assert.Equal(200, geradas.Count);
    }

    [Fact]
    public async Task Redefinicao_libera_o_bloqueio_por_tentativas()
    {
        using var db = TestSupport.NovoContexto();
        var preceptor = TestSupport.Usuario(Roles.Preceptor);
        db.Add(preceptor);
        await db.SaveChangesAsync();
        var protecao = TestSupport.Protecao();
        for (var i = 0; i < 5; i++) protecao.RegistrarFalha($"login:{preceptor.Email}");
        Assert.True(protecao.Bloqueado($"login:{preceptor.Email}", out _));

        await new UsersController(db, new ConflitoTurmasService(db), protecao).ResetarSenha(preceptor.Id);

        Assert.False(protecao.Bloqueado($"login:{preceptor.Email}", out _));
    }

    [Fact]
    public async Task Professor_nao_redefine_a_propria_senha()
    {
        using var db = TestSupport.NovoContexto();
        var professor = TestSupport.Usuario(Roles.Supervisor);
        db.Add(professor);
        await db.SaveChangesAsync();
        var controller = new UsersController(db, new ConflitoTurmasService(db), TestSupport.Protecao())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, professor.Id.ToString())], "teste"))
                }
            }
        };

        Assert.IsType<BadRequestObjectResult>(await controller.ResetarSenha(professor.Id));
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
