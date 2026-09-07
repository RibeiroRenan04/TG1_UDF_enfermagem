using EstagioCheck.API.Controllers;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>
/// Regras do código de presença da atividade remota. O código comprova que o aluno
/// acessou a atividade dentro do prazo — mas sozinho ele não autoriza nada: o
/// registro confere ainda o grupo, a janela, a atividade em aberto, a participação
/// única e a tarefa exigida.
/// </summary>
public class AtividadeRemotaTests
{
    private sealed record Cenario(
        AppDbContext Db,
        ApplicationUser Aluno,
        ApplicationUser Professor,
        StudentGroup Grupo,
        RemoteActivity Atividade);

    private static AtividadesRemotasController Montar(
        AppDbContext db, Guid usuarioId, string papel = Roles.Aluno)
    {
        var controller = new AtividadesRemotasController(db);
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

    /// <summary>
    /// Atividade de hoje com a janela aberta em volta do relógio, para o teste não
    /// depender da hora em que roda.
    /// </summary>
    private static async Task<Cenario> MontarAsync(
        bool exigeTarefa = false, string? tipoTarefa = null, bool ativa = true)
    {
        var db = TestSupport.NovoContexto();

        var aluno = TestSupport.Aluno();
        var professor = TestSupport.Usuario(Roles.Supervisor, "Professora");
        var grupo = new StudentGroup { Code = "T01", Name = "Grupo X" };

        var agora = BrasiliaTime.Agora;
        var atividade = new RemoteActivity
        {
            Title = "Estudo dirigido sobre segurança do paciente",
            Description = "Leitura do material e resposta ao questionário.",
            GroupId = grupo.Id,
            ProfessorId = professor.Id,
            ActivityDate = DateOnly.FromDateTime(agora),
            StartTime = TimeOnly.FromDateTime(agora.AddHours(-1)),
            EndTime = TimeOnly.FromDateTime(agora.AddHours(1)),
            EstimatedHours = 2,
            PresenceCode = "ENF-7K92",
            RequiresTask = exigeTarefa,
            TaskType = tipoTarefa,
            Ativo = ativa
        };

        db.AddRange(aluno, professor, grupo, atividade);
        db.Add(new GroupMembership { StudentId = aluno.Id, GroupId = grupo.Id });
        await db.SaveChangesAsync();

        return new Cenario(db, aluno, professor, grupo, atividade);
    }

    private static RegistrarPresencaRemotaDto Codigo(string codigo, string? resposta = null) =>
        new(codigo, resposta);

    // ── Caminho feliz ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Codigo_valido_registra_participacao_e_credita_horas()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        var resposta = await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        var ok = Assert.IsType<OkObjectResult>(resposta.Result);
        var resultado = Assert.IsType<PresencaRemotaResultadoDto>(ok.Value);
        Assert.Equal(2, resultado.HorasCreditadas);
        Assert.Single(c.Db.RemoteActivityParticipations);
    }

    [Fact]
    public async Task Presenca_remota_gera_o_par_de_pontos_que_conta_a_carga_horaria()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        var pontos = await c.Db.AttendanceRecords
            .Where(r => r.StudentId == c.Aluno.Id)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync();

        Assert.Equal(2, pontos.Count);
        Assert.Equal("check_in", pontos[0].Type);
        Assert.Equal("check_out", pontos[1].Type);
        // O ponto remoto não tem unidade nem coordenadas: ele não passou por geofence.
        Assert.All(pontos, p => Assert.Null(p.LocationId));
        Assert.All(pontos, p => Assert.Equal(c.Atividade.Id, p.RemoteActivityId));
        Assert.Equal(2, (pontos[1].RecordedAt - pontos[0].RecordedAt).TotalHours, 3);
    }

    [Fact]
    public async Task Codigo_e_aceito_sem_hifen_e_em_minusculas()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        var resposta = await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo(" enf7k92 "));

        Assert.IsType<OkObjectResult>(resposta.Result);
    }

    // ── Recusas ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task Codigo_inexistente_e_recusado()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        var resposta = await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-0000"));

        Assert.IsType<NotFoundObjectResult>(resposta.Result);
    }

    [Fact]
    public async Task Aluno_de_outro_grupo_nao_registra_presenca()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        var outroGrupo = new StudentGroup { Code = "T02", Name = "Grupo Y" };
        var intruso = TestSupport.Aluno("Aluno de outro grupo", "87654321");
        c.Db.AddRange(outroGrupo, intruso);
        c.Db.Add(new GroupMembership { StudentId = intruso.Id, GroupId = outroGrupo.Id });
        await c.Db.SaveChangesAsync();

        var resposta = await Montar(c.Db, intruso.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        var recusa = Assert.IsType<ObjectResult>(resposta.Result);
        Assert.Equal(403, recusa.StatusCode);
        Assert.Empty(c.Db.RemoteActivityParticipations);
    }

    [Fact]
    public async Task Codigo_vale_uma_vez_so_por_aluno()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));
        var segunda = await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        Assert.IsType<ConflictObjectResult>(segunda.Result);
        Assert.Single(c.Db.RemoteActivityParticipations);
    }

    [Fact]
    public async Task Atividade_encerrada_invalida_o_codigo()
    {
        var c = await MontarAsync(ativa: false);
        using var _ = c.Db;

        var resposta = await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
    }

    [Fact]
    public async Task Fora_da_janela_de_horario_o_codigo_nao_vale()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        // A janela é empurrada para o passado: o prazo já encerrou.
        var atividade = await c.Db.RemoteActivities.FirstAsync();
        var agora = BrasiliaTime.Agora;
        atividade.StartTime = TimeOnly.FromDateTime(agora.AddHours(-4));
        atividade.EndTime = TimeOnly.FromDateTime(agora.AddHours(-3));
        await c.Db.SaveChangesAsync();

        var resposta = await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
    }

    [Fact]
    public async Task Atividade_com_tarefa_exige_a_entrega_junto_do_codigo()
    {
        var c = await MontarAsync(exigeTarefa: true, tipoTarefa: TipoTarefaRemota.Discursiva);
        using var _ = c.Db;

        var semResposta = await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));
        Assert.IsType<BadRequestObjectResult>(semResposta.Result);

        var comResposta = await Montar(c.Db, c.Aluno.Id)
            .RegistrarPresenca(Codigo("ENF-7K92", "Resposta do estudo dirigido."));
        Assert.IsType<OkObjectResult>(comResposta.Result);
    }

    [Fact]
    public async Task Confirmacao_de_leitura_dispensa_texto_de_resposta()
    {
        var c = await MontarAsync(exigeTarefa: true, tipoTarefa: TipoTarefaRemota.Leitura);
        using var _ = c.Db;

        var resposta = await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        Assert.IsType<OkObjectResult>(resposta.Result);
    }

    [Fact]
    public async Task Aluno_sem_turma_nao_registra_presenca_remota()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        var solto = TestSupport.Aluno("Sem turma", "99999999");
        c.Db.Add(solto);
        await c.Db.SaveChangesAsync();

        var resposta = await Montar(c.Db, solto.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
    }

    // ── Gestão da atividade ───────────────────────────────────────────────────
    [Fact]
    public async Task Atividade_com_participacoes_nao_pode_ser_excluida()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        var resposta = await Montar(c.Db, c.Professor.Id, Roles.Supervisor).Delete(c.Atividade.Id);

        Assert.IsType<ConflictObjectResult>(resposta);
    }

    [Fact]
    public async Task Encerrar_invalida_o_codigo_sem_apagar_as_participacoes()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));
        await Montar(c.Db, c.Professor.Id, Roles.Supervisor).Encerrar(c.Atividade.Id);

        var outroAluno = TestSupport.Aluno("Outro", "11112222");
        c.Db.Add(outroAluno);
        c.Db.Add(new GroupMembership { StudentId = outroAluno.Id, GroupId = c.Grupo.Id });
        await c.Db.SaveChangesAsync();

        var resposta = await Montar(c.Db, outroAluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
        Assert.Single(c.Db.RemoteActivityParticipations);
    }

    [Fact]
    public async Task Novo_codigo_invalida_o_anterior()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        await Montar(c.Db, c.Professor.Id, Roles.Supervisor).NovoCodigo(c.Atividade.Id);

        var resposta = await Montar(c.Db, c.Aluno.Id).RegistrarPresenca(Codigo("ENF-7K92"));

        Assert.IsType<NotFoundObjectResult>(resposta.Result);
    }

    [Fact]
    public void Codigo_gerado_evita_caracteres_ambiguos()
    {
        for (var i = 0; i < 200; i++)
        {
            var codigo = RemoteActivity.GerarCodigo();
            Assert.StartsWith("ENF-", codigo);
            Assert.Equal(8, codigo.Length);
            Assert.DoesNotContain(codigo["ENF-".Length..], c => c is '0' or 'O' or '1' or 'I');
        }
    }
}
