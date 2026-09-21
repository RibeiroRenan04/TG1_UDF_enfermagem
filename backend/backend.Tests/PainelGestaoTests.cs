using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>
/// Indicadores do painel do professor. O que importa fixar: "esperado" segue a
/// mesma programação do check-in (feriado não cobra presença, turma não cobra
/// antes de o aluno entrar nela), e as horas seguem a conta do certificado.
/// </summary>
public class PainelGestaoTests
{
    private static readonly DateOnly Hoje = BrasiliaTime.Hoje;

    private sealed record Cenario(AppDbContext Db, StudentGroup Turma, RotationSchedule Escala, Location Ubs);

    /// <summary>Turma com rodízio pela manhã em todos os dias da semana, cobrindo as duas últimas semanas.</summary>
    private static async Task<Cenario> MontarAsync()
    {
        var db = TestSupport.NovoContexto();
        var turma = new StudentGroup { Code = "T01", Name = "Saúde Coletiva" };
        var ubs = TestSupport.Unidade("UBS Sobradinho");
        ubs.Latitude = -15.65;
        ubs.Longitude = -47.79;
        ubs.StatusGeocodificacao = StatusGeocodificacao.Sucesso;
        var preceptor = TestSupport.Usuario(Roles.Preceptor, "Preceptor");

        var escala = new RotationSchedule
        {
            GroupId = turma.Id, LocationId = ubs.Id, PreceptorId = preceptor.Id,
            Shift = Turnos.Manha, PeriodLabel = "Rodízio",
            StartDate = Hoje.AddDays(-30), EndDate = Hoje.AddDays(30),
            ActivityType = "assistencia", RequiredHours = 10
        };
        // Todo dia da semana presencial: o teste não depende de hoje cair num dia útil.
        foreach (var dia in Enumerable.Range(0, 7))
            escala.Days.Add(new RotationDaySchedule { ScheduleId = escala.Id, DayOfWeek = dia, Mode = ModoAtividade.Presencial });

        db.AddRange(turma, ubs, preceptor, escala);
        await db.SaveChangesAsync();
        return new Cenario(db, turma, escala, ubs);
    }

    private static ApplicationUser AlunoNaTurma(Cenario c, string nome, string rgm, DateTime? entrada = null)
    {
        var aluno = TestSupport.Aluno(nome, rgm);
        c.Db.Add(aluno);
        c.Db.Add(new GroupMembership
        {
            StudentId = aluno.Id, GroupId = c.Turma.Id,
            CreatedAt = entrada ?? DateTime.UtcNow.AddDays(-60)
        });
        return aluno;
    }

    private static void CheckIn(Cenario c, ApplicationUser aluno, DateOnly data, string status = "aprovado", int hora = 8) =>
        c.Db.Add(new AttendanceRecord
        {
            StudentId = aluno.Id, ScheduleId = c.Escala.Id, LocationId = c.Ubs.Id,
            Type = "check_in", Status = status, RecordedAt = data.ToDateTime(new TimeOnly(hora, 0))
        });

    private static PainelGestaoService Servico(AppDbContext db) => new(db, new ProgramacaoService(db));

    [Fact]
    public async Task Presenca_de_hoje_separa_quem_registrou_de_quem_falta()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        var presente = AlunoNaTurma(c, "Ana Presente", "11111111");
        AlunoNaTurma(c, "Bruno Ausente", "22222222");
        CheckIn(c, presente, Hoje);
        await c.Db.SaveChangesAsync();

        var painel = await Servico(c.Db).MontarAsync();

        Assert.Equal(2, painel.Hoje.Esperados);
        Assert.Equal(1, painel.Hoje.Registrados);
        var manha = Assert.Single(painel.Hoje.PorTurno);
        Assert.Equal(Turnos.Manha, manha.Turno);
        var falta = Assert.Single(painel.Hoje.AlunosSemRegistro);
        Assert.Equal("Bruno Ausente", falta.Nome);
        Assert.Equal("UBS Sobradinho", falta.Unidade);
    }

    [Fact]
    public async Task Feriado_da_faculdade_nao_conta_como_dia_esperado()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        AlunoNaTurma(c, "Ana", "11111111");
        var ontem = Hoje.AddDays(-1);
        c.Db.Add(new CalendarException
        {
            Type = TipoExcecao.Feriado, Scope = AbrangenciaExcecao.Faculdade,
            StartDate = ontem, EndDate = ontem, Description = "Feriado"
        });
        await c.Db.SaveChangesAsync();

        var painel = await Servico(c.Db).MontarAsync();

        var diaDoFeriado = painel.UltimosDias.Single(d => d.Data == ontem);
        Assert.Equal(0, diaDoFeriado.Esperados);
        Assert.Null(diaDoFeriado.Percentual);
        // Os outros 13 dias cobram presença e ela não veio: 13 turnos sem registro.
        Assert.Equal(13, Assert.Single(painel.MaisTurnosSemRegistro).Quantidade);
    }

    [Fact]
    public async Task Turma_nao_cobra_presenca_de_antes_da_entrada_do_aluno()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        // Entrou na turma há 3 dias: só 3 dias encerrados cobram presença dele.
        AlunoNaTurma(c, "Novato", "33333333", entrada: DateTime.UtcNow.AddDays(-3));
        await c.Db.SaveChangesAsync();

        var painel = await Servico(c.Db).MontarAsync();

        Assert.Equal(3, Assert.Single(painel.MaisTurnosSemRegistro).Quantidade);
        Assert.Equal(3, painel.UltimosDias.Sum(d => d.Esperados));
    }

    [Fact]
    public async Task Hoje_fica_fora_da_tendencia_porque_o_dia_nao_acabou()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        AlunoNaTurma(c, "Ana", "11111111");
        await c.Db.SaveChangesAsync();

        var painel = await Servico(c.Db).MontarAsync();

        Assert.Equal(PainelGestaoService.DiasDeHistorico, painel.UltimosDias.Count);
        Assert.DoesNotContain(painel.UltimosDias, d => d.Data == Hoje);
    }

    [Fact]
    public async Task Progresso_usa_a_mesma_conta_do_certificado()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        var concluiu = AlunoNaTurma(c, "Concluiu", "11111111");
        AlunoNaTurma(c, "Comecou", "22222222");
        var semTurma = TestSupport.Aluno("Sem Turma", "33333333");
        c.Db.Add(semTurma);

        // 10 horas aprovadas (8h às 18h) — a carga exigida do rodízio.
        var dia = Hoje.AddDays(-2);
        CheckIn(c, concluiu, dia);
        c.Db.Add(new AttendanceRecord
        {
            StudentId = concluiu.Id, ScheduleId = c.Escala.Id, LocationId = c.Ubs.Id,
            Type = "check_out", Status = "aprovado", RecordedAt = dia.ToDateTime(new TimeOnly(18, 0))
        });
        await c.Db.SaveChangesAsync();

        var progresso = (await Servico(c.Db).MontarAsync()).ProgressoCarga;

        Assert.Equal(1, progresso.Elegiveis);
        Assert.Equal(1, progresso.Faixas.Single(f => f.Rotulo == "Concluída").Alunos);
        Assert.Equal(1, progresso.Faixas.Single(f => f.Rotulo == "Até 25%").Alunos);
        Assert.Equal(1, progresso.SemCargaDefinida);
    }

    [Fact]
    public async Task Rodizio_em_unidade_sem_localizacao_vira_alerta_critico()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        AlunoNaTurma(c, "Ana", "11111111");
        c.Db.Add(TestSupport.Aluno("Sem Turma", "22222222"));
        c.Ubs.Latitude = 0;
        c.Ubs.Longitude = 0;
        c.Ubs.StatusGeocodificacao = StatusGeocodificacao.NaoEncontrado;
        await c.Db.SaveChangesAsync();

        var alertas = (await Servico(c.Db).MontarAsync()).Configuracao;

        var critico = alertas.Single(a => a.Codigo == "rodizio_unidade_sem_localizacao");
        Assert.Equal(1, critico.Quantidade);
        Assert.Equal("critico", critico.Severidade);
        Assert.Equal(1, alertas.Single(a => a.Codigo == "aluno_sem_turma").Quantidade);
        Assert.Equal(1, alertas.Single(a => a.Codigo == "unidade_sem_localizacao").Quantidade);
        Assert.Equal(0, alertas.Single(a => a.Codigo == "turma_sem_rodizio").Quantidade);
    }
}
