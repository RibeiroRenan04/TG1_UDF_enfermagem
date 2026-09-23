using EstagioCheck.API.Controllers;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>Turma nova não desfaz a anterior, agenda impossível é recusada e a programação enxerga as duas.</summary>
public class MultiplasTurmasTests
{
    private static GroupsController MontarGrupos(AppDbContext db) =>
        new(db, new ConflitoTurmasService(db));

    private static UsersController MontarUsuarios(AppDbContext db) =>
        new(db, new ConflitoTurmasService(db));

    private static RotationSchedule Escala(
        StudentGroup turma, Location local, string turno,
        DateOnly inicio, DateOnly fim, params int[] dias)
    {
        var escala = new RotationSchedule
        {
            GroupId = turma.Id,
            LocationId = local.Id,
            Shift = turno,
            PeriodLabel = $"{inicio:dd/MM} a {fim:dd/MM}",
            StartDate = inicio,
            EndDate = fim,
            ActivityType = "assistencia",
            RequiredHours = 80
        };

        foreach (var dia in dias)
            escala.Days.Add(new RotationDaySchedule
            {
                ScheduleId = escala.Id,
                DayOfWeek = dia,
                Mode = ModoAtividade.Presencial
            });

        return escala;
    }

    [Fact]
    public async Task Vincular_a_segunda_turma_preserva_a_primeira()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        var ubs = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var hospital = new StudentGroup { Code = "T02", Name = "Estágio Hospitalar" };
        db.AddRange(aluno, ubs, hospital);
        db.Add(new GroupMembership { StudentId = aluno.Id, GroupId = ubs.Id });
        await db.SaveChangesAsync();

        var resposta = await MontarGrupos(db).AddMember(hospital.Id, aluno.Id);

        Assert.IsType<NoContentResult>(resposta);
        var turmas = await db.GroupMemberships.Where(m => m.StudentId == aluno.Id).ToListAsync();
        Assert.Equal(2, turmas.Count);
        Assert.Contains(turmas, m => m.GroupId == ubs.Id);
        Assert.Contains(turmas, m => m.GroupId == hospital.Id);
    }

    [Fact]
    public async Task Vincular_duas_vezes_a_mesma_turma_nao_duplica_o_vinculo()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        var turma = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        db.AddRange(aluno, turma);
        await db.SaveChangesAsync();

        var controller = MontarGrupos(db);
        await controller.AddMember(turma.Id, aluno.Id);
        await controller.AddMember(turma.Id, aluno.Id);

        Assert.Single(await db.GroupMemberships.Where(m => m.StudentId == aluno.Id).ToListAsync());
    }

    [Fact]
    public async Task Desvincular_de_uma_turma_nao_mexe_na_outra()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        var ubs = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var hospital = new StudentGroup { Code = "T02", Name = "Estágio Hospitalar" };
        db.AddRange(aluno, ubs, hospital);
        db.AddRange(
            new GroupMembership { StudentId = aluno.Id, GroupId = ubs.Id },
            new GroupMembership { StudentId = aluno.Id, GroupId = hospital.Id });
        await db.SaveChangesAsync();

        await MontarGrupos(db).RemoveMember(hospital.Id, aluno.Id);

        var restantes = await db.GroupMemberships.Where(m => m.StudentId == aluno.Id).ToListAsync();
        Assert.Equal(ubs.Id, Assert.Single(restantes).GroupId);
    }

    [Fact]
    public async Task Turnos_diferentes_no_mesmo_periodo_sao_permitidos()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        var manha = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var tarde = new StudentGroup { Code = "T02", Name = "Estágio Hospitalar" };
        var ubs = TestSupport.Unidade("UBS Sobradinho");
        var hospital = TestSupport.Unidade("Hospital de Base");
        db.AddRange(aluno, manha, tarde, ubs, hospital);
        db.AddRange(
            Escala(manha, ubs, Turnos.Manha, new(2026, 9, 7), new(2026, 9, 18), 1, 2, 3, 4, 5),
            Escala(tarde, hospital, Turnos.Tarde, new(2026, 9, 7), new(2026, 9, 18), 1, 2, 3, 4, 5));
        db.Add(new GroupMembership { StudentId = aluno.Id, GroupId = manha.Id });
        await db.SaveChangesAsync();

        var resposta = await MontarGrupos(db).AddMember(tarde.Id, aluno.Id);

        Assert.IsType<NoContentResult>(resposta);
        Assert.Equal(2, await db.GroupMemberships.CountAsync(m => m.StudentId == aluno.Id));
    }

    [Fact]
    public async Task Dias_alternados_no_mesmo_turno_sao_permitidos()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        var segQua = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var terQui = new StudentGroup { Code = "T02", Name = "Estágio Hospitalar" };
        var ubs = TestSupport.Unidade("UBS Sobradinho");
        var hospital = TestSupport.Unidade("Hospital de Base");
        db.AddRange(aluno, segQua, terQui, ubs, hospital);
        db.AddRange(
            Escala(segQua, ubs, Turnos.Manha, new(2026, 9, 7), new(2026, 9, 18), 1, 3),
            Escala(terQui, hospital, Turnos.Manha, new(2026, 9, 7), new(2026, 9, 18), 2, 4));
        db.Add(new GroupMembership { StudentId = aluno.Id, GroupId = segQua.Id });
        await db.SaveChangesAsync();

        var resposta = await MontarGrupos(db).AddMember(terQui.Id, aluno.Id);

        Assert.IsType<NoContentResult>(resposta);
    }

    [Fact]
    public async Task Sobreposicao_exata_de_horario_e_recusada()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        var primeira = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var segunda = new StudentGroup { Code = "T02", Name = "Estágio Hospitalar" };
        var ubs = TestSupport.Unidade("UBS Sobradinho");
        var hospital = TestSupport.Unidade("Hospital de Base");
        db.AddRange(aluno, primeira, segunda, ubs, hospital);
        db.AddRange(
            Escala(primeira, ubs, Turnos.Manha, new(2026, 9, 7), new(2026, 9, 18), 1, 2, 3),
            Escala(segunda, hospital, Turnos.Manha, new(2026, 9, 14), new(2026, 9, 25), 3, 4, 5));
        db.Add(new GroupMembership { StudentId = aluno.Id, GroupId = primeira.Id });
        await db.SaveChangesAsync();

        var resposta = await MontarGrupos(db).AddMember(segunda.Id, aluno.Id);

        var conflito = Assert.IsType<ConflictObjectResult>(resposta);
        Assert.Contains("Conflito de agenda", conflito.Value!.ToString());
        Assert.Single(await db.GroupMemberships.Where(m => m.StudentId == aluno.Id).ToListAsync());
    }

    [Fact]
    public async Task Assign_group_com_lista_define_as_turmas_do_aluno()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        var a = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var b = new StudentGroup { Code = "T02", Name = "Estágio Hospitalar" };
        var c = new StudentGroup { Code = "T03", Name = "Reposição" };
        db.AddRange(aluno, a, b, c);
        db.Add(new GroupMembership { StudentId = aluno.Id, GroupId = c.Id });
        await db.SaveChangesAsync();

        await MontarUsuarios(db).AssignGroup(aluno.Id, new AssignGroupDto(null, [a.Id, b.Id]));

        var turmas = await db.GroupMemberships
            .Where(m => m.StudentId == aluno.Id).Select(m => m.GroupId).ToListAsync();
        Assert.Equal(2, turmas.Count);
        Assert.Contains(a.Id, turmas);
        Assert.Contains(b.Id, turmas);
        Assert.DoesNotContain(c.Id, turmas);
    }

    private static VinculoLoteResultadoDto Resultado(ActionResult<VinculoLoteResultadoDto> r) =>
        (VinculoLoteResultadoDto)((ObjectResult)r.Result!).Value!;

    [Fact]
    public async Task Turma_inteira_e_vinculada_numa_chamada_so()
    {
        using var db = TestSupport.NovoContexto();
        var turma = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var alunos = Enumerable.Range(1, 100).Select(i => TestSupport.Aluno($"Aluno {i}", $"{50000000 + i}")).ToList();
        db.Add(turma);
        db.AddRange(alunos);
        await db.SaveChangesAsync();

        var r = Resultado(await MontarGrupos(db).AtualizarMembros(turma.Id,
            new VinculoLoteDto([.. alunos.Select(a => a.Id)], null)));

        Assert.Equal(100, r.Vinculados);
        Assert.Empty(r.Recusados);
        Assert.Equal(100, await db.GroupMemberships.CountAsync(m => m.GroupId == turma.Id));
    }

    [Fact]
    public async Task Lote_grava_os_validos_e_devolve_cada_recusado_com_o_motivo()
    {
        using var db = TestSupport.NovoContexto();
        var manha = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var outraManha = new StudentGroup { Code = "T02", Name = "Reposição" };
        var ubs = TestSupport.Unidade("UBS Sobradinho");
        var livre = TestSupport.Aluno("Aluno Livre", "11111111");
        var ocupado = TestSupport.Aluno("Aluno Ocupado", "22222222");
        var preceptor = TestSupport.Usuario(Roles.Preceptor, "Preceptor");
        db.AddRange(manha, outraManha, ubs, livre, ocupado, preceptor);
        db.AddRange(
            Escala(manha, ubs, Turnos.Manha, new(2026, 9, 7), new(2026, 9, 18), 1, 2, 3),
            Escala(outraManha, ubs, Turnos.Manha, new(2026, 9, 7), new(2026, 9, 18), 1, 2, 3));
        db.Add(new GroupMembership { StudentId = ocupado.Id, GroupId = outraManha.Id });
        await db.SaveChangesAsync();

        var r = Resultado(await MontarGrupos(db).AtualizarMembros(manha.Id,
            new VinculoLoteDto([livre.Id, ocupado.Id, preceptor.Id], null)));

        Assert.Equal(1, r.Vinculados);
        Assert.Equal(2, r.Recusados.Count);
        Assert.Contains(r.Recusados, x => x.StudentId == ocupado.Id && x.Motivo.Contains("Conflito de agenda"));
        Assert.Contains(r.Recusados, x => x.StudentId == preceptor.Id && x.Motivo.Contains("Apenas alunos"));
        Assert.True(await db.GroupMemberships.AnyAsync(m => m.StudentId == livre.Id && m.GroupId == manha.Id));
    }

    [Fact]
    public async Task Lote_desvincula_so_desta_turma_e_repetir_nao_duplica()
    {
        using var db = TestSupport.NovoContexto();
        var t1 = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var t2 = new StudentGroup { Code = "T02", Name = "Estágio Hospitalar" };
        var a = TestSupport.Aluno("Aluno A", "33333333");
        var b = TestSupport.Aluno("Aluno B", "44444444");
        db.AddRange(t1, t2, a, b);
        db.AddRange(
            new GroupMembership { StudentId = a.Id, GroupId = t1.Id },
            new GroupMembership { StudentId = a.Id, GroupId = t2.Id });
        await db.SaveChangesAsync();

        var controller = MontarGrupos(db);
        var r = Resultado(await controller.AtualizarMembros(t1.Id, new VinculoLoteDto([b.Id], [a.Id])));
        var repetido = Resultado(await controller.AtualizarMembros(t1.Id, new VinculoLoteDto([b.Id], [a.Id])));

        Assert.Equal((1, 1), (r.Vinculados, r.Desvinculados));
        Assert.Equal((0, 0), (repetido.Vinculados, repetido.Desvinculados));
        // O aluno A saiu de T01, mas continua em T02.
        Assert.True(await db.GroupMemberships.AnyAsync(m => m.StudentId == a.Id && m.GroupId == t2.Id));
        Assert.False(await db.GroupMemberships.AnyAsync(m => m.StudentId == a.Id && m.GroupId == t1.Id));
    }

    [Fact]
    public async Task Programacao_do_dia_enxerga_o_rodizio_das_duas_turmas()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        var manha = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var tarde = new StudentGroup { Code = "T02", Name = "Estágio Hospitalar" };
        var ubs = TestSupport.Unidade("UBS Sobradinho");
        var hospital = TestSupport.Unidade("Hospital de Base");
        db.AddRange(aluno, manha, tarde, ubs, hospital);
        db.AddRange(
            Escala(manha, ubs, Turnos.Manha, new(2026, 9, 7), new(2026, 9, 18), 1, 2, 3, 4, 5),
            Escala(tarde, hospital, Turnos.Tarde, new(2026, 9, 7), new(2026, 9, 18), 1, 2, 3, 4, 5));
        db.AddRange(
            new GroupMembership { StudentId = aluno.Id, GroupId = manha.Id },
            new GroupMembership { StudentId = aluno.Id, GroupId = tarde.Id });
        await db.SaveChangesAsync();

        var servico = new ProgramacaoService(db);
        var segunda = new DateOnly(2026, 9, 7);

        var deManha = await servico.ObterAsync(aluno.Id, segunda, Turnos.Manha);
        var deTarde = await servico.ObterAsync(aluno.Id, segunda, Turnos.Tarde);

        Assert.Equal(ubs.Id, deManha.Local?.Id);
        Assert.Equal(hospital.Id, deTarde.Local?.Id);
    }
}
