using EstagioCheck.API.Controllers;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>Aluno em duas turmas, cada uma com um preceptor e uma unidade: cada preceptor só vê o seu rodízio.</summary>
public class EscopoPreceptorTests
{
    private sealed record Cenario(
        EstagioCheck.API.Data.AppDbContext Db, ApplicationUser Preceptor, ApplicationUser Aluno,
        RotationSchedule Minha, RotationSchedule Alheia, Location MinhaUbs, Location OutraUbs);

    private static async Task<Cenario> MontarAsync()
    {
        var db = TestSupport.NovoContexto();
        var preceptor = TestSupport.Usuario(Roles.Preceptor, "Preceptora");
        var outro = TestSupport.Usuario(Roles.Preceptor, "Outro");
        var aluno = TestSupport.Aluno();
        var minhaUbs = TestSupport.Unidade("UBS Minha");
        var outraUbs = TestSupport.Unidade("Hospital Outro");
        var t1 = new StudentGroup { Code = "T01", Name = "Estágio" };
        var t2 = new StudentGroup { Code = "PIC", Name = "PIC" };

        RotationSchedule Escala(StudentGroup g, Location l, ApplicationUser p, string turno) => new()
        {
            GroupId = g.Id, LocationId = l.Id, PreceptorId = p.Id, Shift = turno, PeriodLabel = "R1",
            StartDate = new DateOnly(2026, 9, 7), EndDate = new DateOnly(2026, 9, 18),
            ActivityType = "assistencia", RequiredHours = 40
        };

        var minha = Escala(t1, minhaUbs, preceptor, Turnos.Manha);
        var alheia = Escala(t2, outraUbs, outro, Turnos.Tarde);
        db.AddRange(preceptor, outro, aluno, minhaUbs, outraUbs, t1, t2, minha, alheia);
        db.AddRange(new GroupMembership { StudentId = aluno.Id, GroupId = t1.Id },
                    new GroupMembership { StudentId = aluno.Id, GroupId = t2.Id });
        await db.SaveChangesAsync();
        return new Cenario(db, preceptor, aluno, minha, alheia, minhaUbs, outraUbs);
    }

    private static AttendanceRecord Ponto(Guid aluno, Guid? escala, Guid? local) => new()
    {
        StudentId = aluno, ScheduleId = escala, LocationId = local, Type = "check_in",
        Status = "irregular", RecordedAt = new DateTime(2026, 9, 8, 8, 0, 0)
    };

    [Fact]
    public async Task Pontos_de_outro_rodizio_do_mesmo_aluno_ficam_de_fora()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        c.Db.AddRange(
            Ponto(c.Aluno.Id, c.Minha.Id, c.MinhaUbs.Id),
            Ponto(c.Aluno.Id, c.Alheia.Id, c.OutraUbs.Id),
            Ponto(c.Aluno.Id, null, c.MinhaUbs.Id),
            Ponto(c.Aluno.Id, null, c.OutraUbs.Id));
        await c.Db.SaveChangesAsync();

        var escopo = await new EscopoPreceptorService(c.Db).CarregarAsync(c.Preceptor.Id);
        var visiveis = await EscopoPreceptorService.FiltrarPontos(c.Db.AttendanceRecords, escopo).ToListAsync();

        Assert.Equal(2, visiveis.Count);
        Assert.All(visiveis, p => Assert.Equal(c.MinhaUbs.Id, p.LocationId));
    }

    [Fact]
    public async Task Ocorrencia_do_outro_rodizio_nao_aparece_nem_aceita_ciencia()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        var pontoAlheio = Ponto(c.Aluno.Id, c.Alheia.Id, c.OutraUbs.Id);
        var pontoMeu = Ponto(c.Aluno.Id, c.Minha.Id, c.MinhaUbs.Id);
        var alheia = new PointIrregularity
        {
            StudentId = c.Aluno.Id, AttendanceRecordId = pontoAlheio.Id, Type = "outro",
            OccurredOn = new DateOnly(2026, 9, 8), Description = "Ocorrência no hospital"
        };
        var minha = new PointIrregularity
        {
            StudentId = c.Aluno.Id, AttendanceRecordId = pontoMeu.Id, Type = "outro",
            OccurredOn = new DateOnly(2026, 9, 8), Description = "Ocorrência na UBS"
        };
        c.Db.AddRange(pontoAlheio, pontoMeu, alheia, minha);
        await c.Db.SaveChangesAsync();

        var controller = new IrregularitiesController(
            c.Db, new IrregularidadesPainelService(c.Db), new EscopoPreceptorService(c.Db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, c.Preceptor.Id.ToString()),
                        new Claim(ClaimTypes.Role, Roles.Preceptor)
                    ], "teste"))
                }
            }
        };

        var lista = await controller.GetAll(null, null);
        var itens = Assert.IsAssignableFrom<IEnumerable<DTOs.IrregularityDto>>(((ObjectResult)lista.Result!).Value);
        Assert.Equal(minha.Id, Assert.Single(itens).Id);

        var ciencia = await controller.PreceptorReview(alheia.Id, new DTOs.PreceptorReviewIrregularityDto("ok"));
        Assert.IsType<ForbidResult>(ciencia.Result);
    }
}
