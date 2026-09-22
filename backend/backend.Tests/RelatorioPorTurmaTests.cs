using EstagioCheck.API.Controllers;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>
/// Aluno em duas turmas — estágio na UBS pela manhã e PIC às quartas à tarde.
/// O relatório mostra cada turma com os próprios registros e pendências; o
/// certificado é decidido pelo total do aluno, com a conta do certificado.
/// </summary>
public class RelatorioPorTurmaTests
{
    private sealed record Cenario(
        AppDbContext Db, ApplicationUser Aluno, RotationSchedule Estagio, RotationSchedule Pic,
        StudentGroup TurmaEstagio, StudentGroup TurmaPic, DateOnly Segunda, DateOnly Quarta);

    /// <summary>
    /// Semana passada inteira: estágio de segunda a sexta de manhã; PIC só na quarta
    /// à tarde. O aluno registrou a manhã de segunda e de quarta e, se
    /// <paramref name="comPic"/>, a tarde de quarta no PIC.
    /// </summary>
    private static async Task<Cenario> MontarAsync(int horasEstagio = 80, int horasPic = 40, bool comPic = true)
    {
        var db = TestSupport.NovoContexto();

        var quarta = BrasiliaTime.Hoje.AddDays(-7);
        while (quarta.DayOfWeek != DayOfWeek.Wednesday) quarta = quarta.AddDays(-1);
        var segunda = quarta.AddDays(-2);

        var aluno = TestSupport.Aluno();
        var ubs = TestSupport.Unidade("UBS");
        var campus = TestSupport.Unidade("Campus");
        var turmaEstagio = new StudentGroup { Code = "ES1-M01", Name = "Estágio I" };
        var turmaPic = new StudentGroup { Code = "PIC-T01", Name = "PIC" };

        var estagio = new RotationSchedule
        {
            GroupId = turmaEstagio.Id, LocationId = ubs.Id, Shift = Turnos.Manha, PeriodLabel = "R1",
            StartDate = segunda, EndDate = segunda.AddDays(4), ActivityType = "assistencia", RequiredHours = horasEstagio
        };
        var pic = new RotationSchedule
        {
            GroupId = turmaPic.Id, LocationId = campus.Id, Shift = Turnos.Tarde, PeriodLabel = "PIC",
            StartDate = segunda, EndDate = segunda.AddDays(4), ActivityType = "pic", RequiredHours = horasPic
        };
        foreach (var d in new[] { 1, 2, 4, 5 })
            pic.Days.Add(new RotationDaySchedule { ScheduleId = pic.Id, DayOfWeek = d, Mode = ModoAtividade.SemAtividade });
        pic.Days.Add(new RotationDaySchedule { ScheduleId = pic.Id, DayOfWeek = 3, Mode = ModoAtividade.Presencial });

        db.AddRange(aluno, ubs, campus, turmaEstagio, turmaPic, estagio, pic);
        db.AddRange(
            new GroupMembership { StudentId = aluno.Id, GroupId = turmaEstagio.Id, CreatedAt = DateTime.UtcNow.AddDays(-60) },
            new GroupMembership { StudentId = aluno.Id, GroupId = turmaPic.Id, CreatedAt = DateTime.UtcNow.AddDays(-59) });

        Turno(db, aluno, estagio, segunda, 7, 13);
        Turno(db, aluno, estagio, quarta, 7, 13);
        if (comPic) Turno(db, aluno, pic, quarta, 13, 17);
        await db.SaveChangesAsync();

        return new Cenario(db, aluno, estagio, pic, turmaEstagio, turmaPic, segunda, quarta);
    }

    private static void Turno(AppDbContext db, ApplicationUser aluno, RotationSchedule escala, DateOnly dia,
        int entrada, int saida, string status = "aprovado")
    {
        AttendanceRecord Ponto(string tipo, int hora) => new()
        {
            StudentId = aluno.Id, ScheduleId = escala.Id, LocationId = escala.LocationId, Type = tipo,
            Status = status, RecordedAt = dia.ToDateTime(new TimeOnly(hora, 0))
        };
        db.AddRange(Ponto("check_in", entrada), Ponto("check_out", saida));
    }

    private static async Task<ReportRowDto> RelatorioAsync(AppDbContext db, Guid alunoId)
    {
        var controller = new ReportsController(db, new PendenciasService(db, new ProgramacaoService(db)));
        var linhas = (List<ReportRowDto>)((ObjectResult)(await controller.Get()).Result!).Value!;
        return Assert.Single(linhas, l => l.StudentId == alunoId);
    }

    [Fact]
    public async Task Aluno_em_duas_turmas_tem_uma_linha_com_o_detalhe_de_cada_turma()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        var linha = await RelatorioAsync(c.Db, c.Aluno.Id);

        Assert.Equal(2, linha.Turmas.Count);
        var estagio = linha.Turmas.Single(t => t.GroupId == c.TurmaEstagio.Id);
        var pic = linha.Turmas.Single(t => t.GroupId == c.TurmaPic.Id);

        // Cada turma com os próprios registros — antes as duas repetiam o total do aluno.
        Assert.Equal(12, estagio.Hours);
        Assert.Equal(4, pic.Hours);
        Assert.Equal(Turnos.Manha, estagio.Shift);
        Assert.Equal(Turnos.Tarde, pic.Shift);
        Assert.Equal(10, pic.ProgressPercent);

        // Terça, quinta e sexta de manhã sem registro; o PIC foi cumprido na quarta.
        Assert.Equal(3, estagio.PendencyDays);
        Assert.Equal(0, pic.PendencyDays);

        Assert.Equal(16, linha.Hours);
        Assert.Equal(120, linha.Required);
        Assert.Equal(0, linha.HoursOutsideGroups);
        Assert.False(linha.CertificateReleased);
    }

    [Fact]
    public async Task Certificado_e_decidido_pelo_total_do_aluno()
    {
        // 12 h do estágio + 4 h do PIC cobrem as 16 h exigidas no total, embora o PIC
        // sozinho não tenha chegado às 6 h dele.
        var c = await MontarAsync(horasEstagio: 10, horasPic: 6);
        using var _ = c.Db;

        var linha = await RelatorioAsync(c.Db, c.Aluno.Id);

        Assert.True(linha.CertificateReleased);
        Assert.Equal(100, linha.ProgressPercent);
        Assert.True(linha.Turmas.Single(t => t.GroupId == c.TurmaPic.Id).ProgressPercent < 100);

        // O relatório e o certificado usam a mesma conta.
        var certificado = await new CertificateService(c.Db).ObterAsync(c.Aluno.Id);
        Assert.Equal(linha.Hours, certificado!.CompletedHours);
        Assert.True(certificado.Eligible);
    }

    [Fact]
    public async Task So_horas_aprovadas_contam_no_relatorio()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        Turno(c.Db, c.Aluno, c.Estagio, c.Segunda.AddDays(1), 8, 13, status: "pendente");
        await c.Db.SaveChangesAsync();

        var linha = await RelatorioAsync(c.Db, c.Aluno.Id);
        var estagio = linha.Turmas.Single(t => t.GroupId == c.TurmaEstagio.Id);

        Assert.Equal(12, estagio.Hours);
        // A terça tem registro (ainda pendente de validação), então deixa de ser pendência.
        Assert.Equal(2, estagio.PendencyDays);
    }

    [Fact]
    public async Task Turno_do_mesmo_dia_conta_as_duas_turmas_no_certificado()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        // Pareando só por dia, a entrada da manhã de quarta casava com a primeira
        // saída do dia e o turno do PIC não contava: 12 h em vez de 16 h.
        var certificado = await new CertificateService(c.Db).ObterAsync(c.Aluno.Id);

        Assert.Equal(16, certificado!.CompletedHours);
    }

    [Fact]
    public async Task Check_in_da_manha_nao_cobre_o_turno_da_tarde_de_outra_turma()
    {
        var c = await MontarAsync(comPic: false);
        using var _ = c.Db;

        var pendencias = await new PendenciasService(c.Db, new ProgramacaoService(c.Db)).CalcularAsync(c.Aluno.Id);

        // A quarta de manhã foi registrada, mas o PIC da tarde não.
        var picPendente = Assert.Single(pendencias, p => p.ScheduleId == c.Pic.Id);
        Assert.Equal(c.Quarta, picPendente.PendencyDate);
        Assert.Equal(3, pendencias.Count(p => p.ScheduleId == c.Estagio.Id));
    }
}
