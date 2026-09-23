using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>A lista em lote precisa dar exatamente o que o certificado individual dá.</summary>
public class CertificadosLoteTests
{
    [Fact]
    public async Task Lista_em_lote_confere_com_o_certificado_individual()
    {
        using var db = TestSupport.NovoContexto();
        var ubs = TestSupport.Unidade("UBS Sobradinho");
        var estagio = new StudentGroup { Code = "ES1-M05", Name = "Estágio I" };
        var pic = new StudentGroup { Code = "PIC-T01", Name = "PIC" };
        var escala = new RotationSchedule
        {
            GroupId = estagio.Id, LocationId = ubs.Id, Shift = Turnos.Manha, PeriodLabel = "R1",
            StartDate = new DateOnly(2026, 8, 3), EndDate = new DateOnly(2026, 12, 11),
            ActivityType = "assistencia", RequiredHours = 12
        };
        var escalaPic = new RotationSchedule
        {
            GroupId = pic.Id, LocationId = ubs.Id, Shift = Turnos.Tarde, PeriodLabel = "PIC",
            StartDate = new DateOnly(2026, 8, 3), EndDate = new DateOnly(2026, 12, 11),
            ActivityType = "pic", RequiredHours = 4
        };
        var completo = TestSupport.Aluno("Ana Completa", "11111111");
        var parcial = TestSupport.Aluno("Bruno Parcial", "22222222");
        var semTurma = TestSupport.Aluno("Carla Sem Turma", "33333333");
        var preceptor = TestSupport.Usuario(Roles.Preceptor);
        db.AddRange(ubs, estagio, pic, escala, escalaPic, completo, parcial, semTurma, preceptor);
        db.AddRange(
            new GroupMembership { StudentId = completo.Id, GroupId = estagio.Id },
            new GroupMembership { StudentId = completo.Id, GroupId = pic.Id },
            new GroupMembership { StudentId = parcial.Id, GroupId = estagio.Id },
            new GroupMembership { StudentId = preceptor.Id, GroupId = estagio.Id });

        AttendanceRecord Ponto(ApplicationUser a, RotationSchedule e, string tipo, DateTime quando, string status = "aprovado") =>
            new() { StudentId = a.Id, ScheduleId = e.Id, LocationId = ubs.Id, Type = tipo, Status = status, RecordedAt = quando };

        var dia = new DateTime(2026, 9, 8);
        db.AddRange(
            Ponto(completo, escala, "check_in", dia.AddHours(7)), Ponto(completo, escala, "check_out", dia.AddHours(19)),
            Ponto(completo, escalaPic, "check_in", dia.AddDays(1).AddHours(13)), Ponto(completo, escalaPic, "check_out", dia.AddDays(1).AddHours(17)),
            Ponto(parcial, escala, "check_in", dia.AddHours(7)), Ponto(parcial, escala, "check_out", dia.AddHours(13)),
            Ponto(parcial, escala, "check_in", dia.AddDays(1).AddHours(7), "irregular"),
            Ponto(parcial, escala, "check_out", dia.AddDays(1).AddHours(13)));
        await db.SaveChangesAsync();

        var servico = new CertificateService(db);
        var lista = await servico.ListarAsync();

        Assert.Equal(["Ana Completa", "Bruno Parcial"], lista.Select(c => c.StudentName));

        foreach (var doLote in lista)
        {
            var individual = await servico.ObterAsync(doLote.StudentId);
            Assert.NotNull(individual);
            Assert.Equal(individual!.CompletedHours, doLote.CompletedHours);
            Assert.Equal(individual.RequiredHours, doLote.RequiredHours);
            Assert.Equal(individual.Eligible, doLote.Eligible);
            Assert.Equal(individual.GroupName, doLote.GroupName);
            Assert.Equal(individual.VerificationCode, doLote.VerificationCode);
        }

        var ana = lista[0];
        Assert.Equal(16, ana.CompletedHours);
        Assert.Equal(16, ana.RequiredHours);
        Assert.True(ana.Eligible);
        Assert.Equal("Estágio I, PIC", ana.GroupName);

        var bruno = lista[1];
        Assert.Equal(6, bruno.CompletedHours);
        Assert.False(bruno.Eligible);

        Assert.Null(await servico.ObterAsync(preceptor.Id));
    }
}
