using EstagioCheck.API.Controllers;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>
/// Regras de alocação de estagiários. As verificações que importam ficam na API,
/// não na tela: é a API que recusa perfil errado e alocação dupla.
/// </summary>
public class AlocacaoTests
{
    private static AlocacoesController Montar(
        EstagioCheck.API.Data.AppDbContext db, Guid usuarioId, string papel = Roles.Supervisor)
    {
        var controller = new AlocacoesController(db, TestSupport.Logger<AlocacoesController>());
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

    /// <summary>
    /// Listagens devolvem o resultado de um Select (avaliação preguiçosa), não um
    /// List: materializa antes de conferir.
    /// </summary>
    private static List<AlocacaoDto> Lista(ActionResult<List<AlocacaoDto>> resultado) =>
        ((IEnumerable<AlocacaoDto>)((ObjectResult)resultado.Result!).Value!).ToList();

    [Fact]
    public async Task Aluno_e_alocado_com_sucesso()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        var professor = TestSupport.Usuario(Roles.Supervisor, "Professora");
        db.AddRange(unidade, aluno, professor);
        await db.SaveChangesAsync();

        var resposta = await Montar(db, professor.Id)
            .Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, "Primeira alocação"));

        var dto = Corpo(resposta);
        Assert.Equal(aluno.Id, dto.EstagiarioId);
        Assert.Equal(unidade.Id, dto.UnidadeId);
        Assert.True(dto.Ativo);
        Assert.Null(dto.DataFim);
        Assert.Equal(professor.Id, db.StudentAllocations.Single().CreatedById);
    }

    [Theory]
    [InlineData(Roles.Preceptor)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Coordenadora)]
    public async Task Usuario_que_nao_e_aluno_nao_pode_ser_alocado(string papel)
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var naoAluno = TestSupport.Usuario(papel, "Fulano");
        db.AddRange(unidade, naoAluno);
        await db.SaveChangesAsync();

        var resposta = await Montar(db, Guid.NewGuid())
            .Alocar(unidade.Id, new CriarAlocacaoDto(naoAluno.Id, null, null));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
        Assert.Empty(db.StudentAllocations);
    }

    [Fact]
    public async Task Unidade_inexistente_e_recusada()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        db.Add(aluno);
        await db.SaveChangesAsync();

        var resposta = await Montar(db, Guid.NewGuid())
            .Alocar(Guid.NewGuid(), new CriarAlocacaoDto(aluno.Id, null, null));

        Assert.IsType<NotFoundObjectResult>(resposta.Result);
    }

    [Fact]
    public async Task Unidade_inativa_nao_recebe_alocacao()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        unidade.Ativo = false;
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var resposta = await Montar(db, Guid.NewGuid())
            .Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
    }

    [Fact]
    public async Task Aluno_com_alocacao_ativa_nao_recebe_outra_sem_confirmacao()
    {
        using var db = TestSupport.NovoContexto();
        var unidadeA = TestSupport.Unidade("UBS A");
        var unidadeB = TestSupport.Unidade("UBS B");
        var aluno = TestSupport.Aluno();
        db.AddRange(unidadeA, unidadeB, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidadeA.Id, new CriarAlocacaoDto(aluno.Id, null, null));

        var resposta = await controller.Alocar(unidadeB.Id, new CriarAlocacaoDto(aluno.Id, null, null));

        Assert.IsType<ConflictObjectResult>(resposta.Result);
        // Continua com uma só alocação ativa: a original.
        Assert.Single(db.StudentAllocations, a => a.Ativo);
        Assert.Equal(unidadeA.Id, db.StudentAllocations.Single(a => a.Ativo).LocationId);
    }

    [Fact]
    public async Task Realocar_na_mesma_unidade_e_recusado()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null));
        var resposta = await controller.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null));

        Assert.IsType<ConflictObjectResult>(resposta.Result);
    }

    [Fact]
    public async Task Troca_de_unidade_encerra_a_anterior_e_preserva_o_historico()
    {
        using var db = TestSupport.NovoContexto();
        var unidadeA = TestSupport.Unidade("UBS A");
        var unidadeB = TestSupport.Unidade("UBS B");
        var aluno = TestSupport.Aluno();
        db.AddRange(unidadeA, unidadeB, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidadeA.Id, new CriarAlocacaoDto(aluno.Id, null, null));

        var resposta = await controller.Alocar(unidadeB.Id,
            new CriarAlocacaoDto(aluno.Id, null, "Troca autorizada", EncerrarAlocacaoAtual: true));

        var nova = Corpo(resposta);
        Assert.Equal(unidadeB.Id, nova.UnidadeId);

        // O histórico guarda as duas: a antiga encerrada, a nova ativa.
        var todas = db.StudentAllocations.Where(a => a.StudentId == aluno.Id).ToList();
        Assert.Equal(2, todas.Count);

        var antiga = todas.Single(a => a.LocationId == unidadeA.Id);
        Assert.False(antiga.Ativo);
        Assert.NotNull(antiga.EndDate);
        Assert.Single(todas, a => a.Ativo);
    }

    [Fact]
    public async Task Encerrar_alocacao_mantem_o_registro_com_data_fim()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null));

        var resposta = await controller.Encerrar(unidade.Id, aluno.Id,
            new EncerrarAlocacaoDto(null, "Fim do rodízio"));

        var dto = Corpo(resposta);
        Assert.False(dto.Ativo);
        Assert.NotNull(dto.DataFim);
        // O registro não é apagado: é o histórico de onde o aluno esteve.
        Assert.Single(db.StudentAllocations);
    }

    [Fact]
    public async Task Encerrar_alocacao_inexistente_devolve_nao_encontrado()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var resposta = await Montar(db, Guid.NewGuid()).Encerrar(unidade.Id, aluno.Id, null);

        Assert.IsType<NotFoundObjectResult>(resposta.Result);
    }

    [Fact]
    public async Task Aluno_encerrado_pode_ser_alocado_de_novo()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null));
        await controller.Encerrar(unidade.Id, aluno.Id, null);

        var resposta = await controller.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null));

        Assert.IsType<OkObjectResult>(resposta.Result);
        Assert.Equal(2, db.StudentAllocations.Count());
        Assert.Single(db.StudentAllocations, a => a.Ativo);
    }

    [Fact]
    public async Task Aluno_so_enxerga_a_propria_unidade()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno("Aluno A");
        var outro = TestSupport.Aluno("Aluno B");
        db.AddRange(unidade, aluno, outro);
        await db.SaveChangesAsync();

        await Montar(db, Guid.NewGuid()).Alocar(unidade.Id, new CriarAlocacaoDto(outro.Id, null, null));

        var comoAluno = Montar(db, aluno.Id, Roles.Aluno);
        var resposta = await comoAluno.GetUnidadeDoEstagiario(outro.Id);

        Assert.IsType<ForbidResult>(resposta.Result);
    }

    [Fact]
    public async Task Aluno_consulta_a_propria_alocacao()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        await Montar(db, Guid.NewGuid()).Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null));

        var resposta = await Montar(db, aluno.Id, Roles.Aluno).GetUnidadeDoEstagiario(aluno.Id);

        Assert.Equal(unidade.Id, Corpo(resposta).UnidadeId);
    }

    [Fact]
    public async Task Estagiarios_da_unidade_listam_apenas_alocacoes_ativas_por_padrao()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var alunoA = TestSupport.Aluno("Aluno A");
        var alunoB = TestSupport.Aluno("Aluno B");
        db.AddRange(unidade, alunoA, alunoB);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidade.Id, new CriarAlocacaoDto(alunoA.Id, null, null));
        await controller.Alocar(unidade.Id, new CriarAlocacaoDto(alunoB.Id, null, null));
        await controller.Encerrar(unidade.Id, alunoB.Id, null);

        var ativas = Lista(await controller.GetEstagiariosDaUnidade(unidade.Id));
        var todas = Lista(await controller.GetEstagiariosDaUnidade(unidade.Id, incluirEncerradas: true));

        Assert.Single(ativas);
        Assert.Equal(2, todas.Count);
    }

    // ── Alocação por turno ────────────────────────────────────────────────────
    [Fact]
    public async Task Aluno_pode_ser_alocado_em_unidades_diferentes_em_turnos_diferentes()
    {
        using var db = TestSupport.NovoContexto();
        var unidadeManha = TestSupport.Unidade("UBS Manhã");
        var unidadeTarde = TestSupport.Unidade("UBS Tarde");
        var aluno = TestSupport.Aluno();
        db.AddRange(unidadeManha, unidadeTarde, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidadeManha.Id, new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Manha));

        var resposta = await controller.Alocar(unidadeTarde.Id,
            new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Tarde));

        Assert.IsType<OkObjectResult>(resposta.Result);
        // As duas convivem: turnos diferentes, unidades diferentes.
        var ativas = db.StudentAllocations.Where(a => a.Ativo).ToList();
        Assert.Equal(2, ativas.Count);
        Assert.Contains(ativas, a => a.Shift == Turnos.Manha && a.LocationId == unidadeManha.Id);
        Assert.Contains(ativas, a => a.Shift == Turnos.Tarde && a.LocationId == unidadeTarde.Id);
    }

    [Fact]
    public async Task Aluno_nao_pode_ter_duas_alocacoes_no_mesmo_turno()
    {
        using var db = TestSupport.NovoContexto();
        var unidadeA = TestSupport.Unidade("UBS A");
        var unidadeB = TestSupport.Unidade("UBS B");
        var aluno = TestSupport.Aluno();
        db.AddRange(unidadeA, unidadeB, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidadeA.Id, new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Tarde));

        var resposta = await controller.Alocar(unidadeB.Id,
            new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Tarde));

        Assert.IsType<ConflictObjectResult>(resposta.Result);
        Assert.Single(db.StudentAllocations, a => a.Ativo);
    }

    [Fact]
    public async Task Sem_turno_informado_usa_o_turno_cadastrado_do_aluno()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        aluno.Shift = Turnos.Noite;
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var resposta = await Montar(db, Guid.NewGuid())
            .Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null));

        Assert.Equal(Turnos.Noite, Corpo(resposta).Turno);
    }

    [Fact]
    public async Task Turno_invalido_e_recusado()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var resposta = await Montar(db, Guid.NewGuid())
            .Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null, Turno: "madrugada"));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
        Assert.Empty(db.StudentAllocations);
    }

    [Fact]
    public async Task Trocar_de_unidade_encerra_so_a_alocacao_daquele_turno()
    {
        using var db = TestSupport.NovoContexto();
        var unidadeA = TestSupport.Unidade("UBS A");
        var unidadeB = TestSupport.Unidade("UBS B");
        var aluno = TestSupport.Aluno();
        db.AddRange(unidadeA, unidadeB, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidadeA.Id, new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Manha));
        await controller.Alocar(unidadeA.Id, new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Tarde));

        // Transfere só a tarde para a unidade B.
        await controller.Alocar(unidadeB.Id,
            new CriarAlocacaoDto(aluno.Id, null, null, EncerrarAlocacaoAtual: true, Turno: Turnos.Tarde));

        var ativas = db.StudentAllocations.Where(a => a.Ativo).ToList();
        Assert.Equal(2, ativas.Count);
        // A manhã na unidade A continua intacta.
        Assert.Contains(ativas, a => a.Shift == Turnos.Manha && a.LocationId == unidadeA.Id);
        Assert.Contains(ativas, a => a.Shift == Turnos.Tarde && a.LocationId == unidadeB.Id);
    }

    [Fact]
    public async Task Encerrar_sem_turno_com_varias_alocacoes_na_unidade_pede_o_turno()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Manha));
        await controller.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Tarde));

        var resposta = await controller.Encerrar(unidade.Id, aluno.Id, null);

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
        Assert.Equal(2, db.StudentAllocations.Count(a => a.Ativo));
    }

    [Fact]
    public async Task Encerrar_com_turno_nao_afeta_os_demais()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Manha));
        await controller.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Tarde));

        var resposta = await controller.Encerrar(unidade.Id, aluno.Id, null, turno: Turnos.Manha);

        Assert.Equal(Turnos.Manha, Corpo(resposta).Turno);
        Assert.Single(db.StudentAllocations, a => a.Ativo);
        Assert.Equal(Turnos.Tarde, db.StudentAllocations.Single(a => a.Ativo).Shift);
    }

    [Fact]
    public async Task Estagiarios_disponiveis_mostram_os_turnos_ainda_livres()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno();
        db.AddRange(unidade, aluno);
        await db.SaveChangesAsync();

        var controller = Montar(db, Guid.NewGuid());
        await controller.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null, Turno: Turnos.Manha));

        var resposta = await controller.GetDisponiveis(unidade.Id, null);
        var disponivel = ((IEnumerable<EstagiarioDisponivelDto>)((ObjectResult)resposta.Result!).Value!)
            .Single(x => x.Id == aluno.Id);

        Assert.Equal(new[] { Turnos.Tarde, Turnos.Noite }, disponivel.TurnosDisponiveis);
        Assert.Single(disponivel.AlocacoesAtivas, a => a.Turno == Turnos.Manha);
    }

    [Fact]
    public async Task Aluno_so_ve_a_propria_alocacao_na_lista_da_unidade()
    {
        using var db = TestSupport.NovoContexto();
        var unidade = TestSupport.Unidade();
        var aluno = TestSupport.Aluno("Aluno A");
        var colega = TestSupport.Aluno("Aluno B");
        db.AddRange(unidade, aluno, colega);
        await db.SaveChangesAsync();

        var gestao = Montar(db, Guid.NewGuid());
        await gestao.Alocar(unidade.Id, new CriarAlocacaoDto(aluno.Id, null, null));
        await gestao.Alocar(unidade.Id, new CriarAlocacaoDto(colega.Id, null, null));

        var comoAluno = Lista(await Montar(db, aluno.Id, Roles.Aluno).GetEstagiariosDaUnidade(unidade.Id));

        // A relação dos colegas não é dado do aluno.
        Assert.Single(comoAluno);
        Assert.Equal(aluno.Id, comoAluno[0].EstagiarioId);
    }
}
