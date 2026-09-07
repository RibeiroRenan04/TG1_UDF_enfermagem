using EstagioCheck.API.Controllers;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>
/// Travas do registro de ponto definidas na reunião de 06/09/2026:
///   • a descrição das atividades é obrigatória no check-out;
///   • no máximo 1 check-in e 1 check-out por turno para cada aluno.
///
/// O turno de um registro vem da escala vinculada; sem escala, do horário do
/// próprio registro.
/// </summary>
public class PontoTurnoTests
{
    private static AttendanceController Montar(
        EstagioCheck.API.Data.AppDbContext db, Guid usuarioId, string papel = Roles.Aluno)
    {
        var controller = new AttendanceController(db, new GeoService());
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
    /// Registro no local do aluno. Sem LocationId a API não aplica geofence — é o
    /// caminho que isola a regra de turno da regra de distância.
    /// </summary>
    private static CreateAttendanceDto Ponto(string tipo, Guid? escalaId = null, string? descricao = null) =>
        new(0, 0, tipo, escalaId, null, descricao, null, null);

    private static async Task<(EstagioCheck.API.Data.AppDbContext db, ApplicationUser aluno)> ComAlunoAsync()
    {
        var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        db.Add(aluno);
        await db.SaveChangesAsync();
        return (db, aluno);
    }

    // ── Descrição obrigatória no check-out ────────────────────────────────────
    [Fact]
    public async Task Check_out_sem_descricao_e_recusado()
    {
        var (db, aluno) = await ComAlunoAsync();
        using var _ = db;

        var controller = Montar(db, aluno.Id);
        await controller.Create(Ponto("check_in"));

        var resposta = await controller.Create(Ponto("check_out"));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
        Assert.DoesNotContain(db.AttendanceRecords, r => r.Type == "check_out");
    }

    [Fact]
    public async Task Check_out_com_descricao_em_branco_e_recusado()
    {
        var (db, aluno) = await ComAlunoAsync();
        using var _ = db;

        var controller = Montar(db, aluno.Id);
        await controller.Create(Ponto("check_in"));

        var resposta = await controller.Create(Ponto("check_out", descricao: "   "));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
    }

    [Fact]
    public async Task Check_out_com_descricao_e_aceito_e_guarda_o_texto()
    {
        var (db, aluno) = await ComAlunoAsync();
        using var _ = db;

        var controller = Montar(db, aluno.Id);
        await controller.Create(Ponto("check_in"));

        var resposta = await controller.Create(
            Ponto("check_out", descricao: "  Acompanhamento de curativos e triagem.  "));

        Assert.IsType<OkObjectResult>(resposta.Result);
        // O texto é gravado sem os espaços das pontas.
        Assert.Equal("Acompanhamento de curativos e triagem.",
            db.AttendanceRecords.Single(r => r.Type == "check_out").ActivitiesDescription);
    }

    [Fact]
    public async Task Check_in_nao_exige_descricao()
    {
        var (db, aluno) = await ComAlunoAsync();
        using var _ = db;

        var resposta = await Montar(db, aluno.Id).Create(Ponto("check_in"));

        Assert.IsType<OkObjectResult>(resposta.Result);
    }

    // ── 1 check-in e 1 check-out por turno ────────────────────────────────────
    [Fact]
    public async Task Segundo_check_in_no_mesmo_turno_e_recusado()
    {
        var (db, aluno) = await ComAlunoAsync();
        using var _ = db;

        var controller = Montar(db, aluno.Id);
        await controller.Create(Ponto("check_in"));

        var resposta = await controller.Create(Ponto("check_in"));

        Assert.IsType<ConflictObjectResult>(resposta.Result);
        Assert.Single(db.AttendanceRecords);
    }

    [Fact]
    public async Task Segundo_check_out_no_mesmo_turno_e_recusado()
    {
        var (db, aluno) = await ComAlunoAsync();
        using var _ = db;

        var controller = Montar(db, aluno.Id);
        await controller.Create(Ponto("check_in"));
        await controller.Create(Ponto("check_out", descricao: "Atividades do turno."));

        var resposta = await controller.Create(Ponto("check_out", descricao: "De novo."));

        Assert.IsType<ConflictObjectResult>(resposta.Result);
        Assert.Single(db.AttendanceRecords, r => r.Type == "check_out");
    }

    [Fact]
    public async Task Check_out_sem_check_in_no_turno_e_recusado()
    {
        var (db, aluno) = await ComAlunoAsync();
        using var _ = db;

        var resposta = await Montar(db, aluno.Id)
            .Create(Ponto("check_out", descricao: "Atividades do turno."));

        Assert.IsType<BadRequestObjectResult>(resposta.Result);
        Assert.Empty(db.AttendanceRecords);
    }

    [Fact]
    public async Task Turnos_diferentes_aceitam_um_par_de_registros_cada()
    {
        var (db, aluno) = await ComAlunoAsync();
        using var _ = db;

        // Duas escalas hoje: manhã e tarde. O turno do ponto vem da escala.
        var manha = await EscalaAsync(db, Turnos.Manha);
        var tarde = await EscalaAsync(db, Turnos.Tarde);

        var controller = Montar(db, aluno.Id);
        await controller.Create(Ponto("check_in", manha));
        await controller.Create(Ponto("check_out", manha, "Manhã na UBS."));
        await controller.Create(Ponto("check_in", tarde));
        var resposta = await controller.Create(Ponto("check_out", tarde, "Tarde no hospital."));

        Assert.IsType<OkObjectResult>(resposta.Result);
        Assert.Equal(4, db.AttendanceRecords.Count());
    }

    [Fact]
    public async Task Registro_de_ontem_nao_bloqueia_o_ponto_de_hoje()
    {
        var (db, aluno) = await ComAlunoAsync();
        using var _ = db;

        // Check-in aberto de ontem: antes, a tela ficava presa em "check-out"
        // para sempre porque a consulta olhava só o último registro do aluno.
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            StudentId = aluno.Id,
            Type = "check_in",
            RecordedAt = BrasiliaTime.Agora.AddDays(-1),
            Status = "aprovado"
        });
        await db.SaveChangesAsync();

        var resposta = await Montar(db, aluno.Id).Create(Ponto("check_in"));

        Assert.IsType<OkObjectResult>(resposta.Result);
    }

    // ── Situação do turno exposta para a tela ─────────────────────────────────
    [Fact]
    public async Task Status_do_turno_acompanha_o_que_ja_foi_registrado()
    {
        var (db, aluno) = await ComAlunoAsync();
        using var _ = db;

        var controller = Montar(db, aluno.Id);

        var inicial = Corpo(await controller.GetShiftStatus());
        Assert.True(inicial.CanCheckIn);
        Assert.False(inicial.CanCheckOut);

        await controller.Create(Ponto("check_in"));
        var aposEntrada = Corpo(await controller.GetShiftStatus());
        Assert.False(aposEntrada.CanCheckIn);
        Assert.True(aposEntrada.CanCheckOut);

        await controller.Create(Ponto("check_out", descricao: "Atividades do turno."));
        var aposSaida = Corpo(await controller.GetShiftStatus());
        Assert.False(aposSaida.CanCheckIn);
        Assert.False(aposSaida.CanCheckOut);
        Assert.True(aposSaida.ShiftClosed);
        Assert.NotNull(aposSaida.BlockedReason);
    }

    // ── Apoio ─────────────────────────────────────────────────────────────────
    private static async Task<Guid> EscalaAsync(EstagioCheck.API.Data.AppDbContext db, string turno)
    {
        var unidade = TestSupport.Unidade($"Unidade {turno}");
        var grupo = new StudentGroup { Code = $"T-{turno}", Name = $"Turma {turno}" };
        var preceptor = TestSupport.Usuario(Roles.Preceptor);
        db.AddRange(unidade, grupo, preceptor);
        await db.SaveChangesAsync();

        var hoje = BrasiliaTime.Hoje;
        var escala = new RotationSchedule
        {
            GroupId = grupo.Id,
            LocationId = unidade.Id,
            PreceptorId = preceptor.Id,
            Shift = turno,
            PeriodLabel = "2026/2",
            StartDate = hoje,
            EndDate = hoje,
            ActivityType = "assistencia"
        };
        db.RotationSchedules.Add(escala);
        await db.SaveChangesAsync();
        return escala.Id;
    }

    private static T Corpo<T>(ActionResult<T> resultado) where T : class =>
        (T)((ObjectResult)resultado.Result!).Value!;
}
