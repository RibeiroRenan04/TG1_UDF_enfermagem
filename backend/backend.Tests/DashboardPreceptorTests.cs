using EstagioCheck.API.Controllers;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>Os cards do preceptor contam só os alunos das turmas que ele acompanha.</summary>
public class DashboardPreceptorTests
{
    private static DashboardController Montar(AppDbContext db, Guid usuarioId, string papel)
    {
        var programacao = new ProgramacaoService(db);
        var controller = new DashboardController(db, new PendenciasService(db, programacao),
            new PainelGestaoService(db, programacao), new EscopoPreceptorService(db));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
                    new Claim(ClaimTypes.Role, papel)
                ], "teste"))
            }
        };
        return controller;
    }

    private static AttendanceRecord Ponto(Guid aluno, string tipo, DateTime quando, string status = "aprovado",
        RotationSchedule? escala = null) => new()
    {
        StudentId = aluno, Type = tipo, RecordedAt = quando, Status = status, CreatedAt = quando,
        ScheduleId = escala?.Id, LocationId = escala?.LocationId
    };

    [Fact]
    public async Task Preceptor_ve_apenas_registros_e_alunos_das_suas_turmas()
    {
        using var db = TestSupport.NovoContexto();
        var preceptor = TestSupport.Usuario(Roles.Preceptor, "Preceptora");
        var outroPreceptor = TestSupport.Usuario(Roles.Preceptor, "Outro");
        var meu = TestSupport.Aluno("Aluno da minha turma");
        var alheio = TestSupport.Aluno("Aluno de outra turma");
        var ubs = TestSupport.Unidade();
        var minhaTurma = new StudentGroup { Code = "T01", Name = "Minha" };
        var outraTurma = new StudentGroup { Code = "T02", Name = "Outra" };

        RotationSchedule Escala(StudentGroup g, ApplicationUser p) => new()
        {
            GroupId = g.Id, LocationId = ubs.Id, PreceptorId = p.Id, Shift = Turnos.Manha,
            PeriodLabel = "R1", StartDate = new DateOnly(2026, 9, 7), EndDate = new DateOnly(2026, 9, 18),
            ActivityType = "assistencia", RequiredHours = 80
        };

        var minhaEscala = Escala(minhaTurma, preceptor);
        var outraEscala = Escala(outraTurma, outroPreceptor);
        db.AddRange(preceptor, outroPreceptor, meu, alheio, ubs, minhaTurma, outraTurma, minhaEscala, outraEscala);
        db.AddRange(new GroupMembership { StudentId = meu.Id, GroupId = minhaTurma.Id },
                    new GroupMembership { StudentId = alheio.Id, GroupId = outraTurma.Id });

        var dia = new DateTime(2026, 9, 8, 7, 0, 0);
        db.AddRange(Ponto(meu.Id, "check_in", dia, escala: minhaEscala), Ponto(meu.Id, "check_out", dia.AddHours(6), escala: minhaEscala),
                    Ponto(alheio.Id, "check_in", dia, escala: outraEscala), Ponto(alheio.Id, "check_out", dia.AddHours(6), escala: outraEscala),
                    Ponto(alheio.Id, "check_in", dia.AddDays(1), "irregular", outraEscala));
        await db.SaveChangesAsync();

        var resposta = await Montar(db, preceptor.Id, Roles.Preceptor).GetStats();
        var stats = (DashboardStatsDto)((ObjectResult)resposta.Result!).Value!;

        Assert.Equal(2, stats.Total);
        Assert.Equal(0, stats.Irregular);
        Assert.Equal(6, stats.Hours);
        Assert.Equal(1, stats.TotalStudents);
    }

    [Fact]
    public async Task Professor_continua_vendo_a_faculdade_inteira()
    {
        using var db = TestSupport.NovoContexto();
        var professor = TestSupport.Usuario(Roles.Supervisor, "Professora");
        var a = TestSupport.Aluno("A");
        var b = TestSupport.Aluno("B");
        db.AddRange(professor, a, b);
        var dia = new DateTime(2026, 9, 8, 7, 0, 0);
        db.AddRange(Ponto(a.Id, "check_in", dia), Ponto(b.Id, "check_in", dia));
        await db.SaveChangesAsync();

        var resposta = await Montar(db, professor.Id, Roles.Supervisor).GetStats();
        var stats = (DashboardStatsDto)((ObjectResult)resposta.Result!).Value!;

        Assert.Equal(2, stats.Total);
        Assert.Equal(2, stats.TotalStudents);
    }
}
