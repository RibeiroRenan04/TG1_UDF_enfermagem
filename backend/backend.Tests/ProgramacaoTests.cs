using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>
/// A programação do dia é a nova pergunta do sistema: onde o aluno deveria estar e
/// o que deveria fazer. Estes testes cobrem as três camadas que a compõem — o
/// rodízio, a regra do dia da semana e as exceções do calendário — e a ordem em que
/// uma vence a outra.
/// </summary>
public class ProgramacaoTests
{
    // 07/09/2026 é uma segunda-feira; 11/09/2026, uma sexta.
    private static readonly DateOnly Segunda = new(2026, 9, 7);
    private static readonly DateOnly Sexta = new(2026, 9, 11);
    private static readonly DateOnly Domingo = new(2026, 9, 13);

    private sealed record Cenario(
        AppDbContext Db,
        ApplicationUser Aluno,
        StudentGroup Grupo,
        RotationSchedule Escala,
        Location Ubs,
        Location Faculdade);

    /// <summary>Aluno de uma turma com rodízio de duas semanas na UBS, turno da manhã.</summary>
    private static async Task<Cenario> MontarAsync()
    {
        var db = TestSupport.NovoContexto();

        var aluno = TestSupport.Aluno();
        aluno.Course = "Enfermagem";

        var grupo = new StudentGroup { Code = "T01", Name = "Grupo X" };
        var ubs = TestSupport.Unidade("UBS Sobradinho");
        var faculdade = TestSupport.Unidade("Faculdade");
        faculdade.IsInstitution = true;

        var escala = new RotationSchedule
        {
            GroupId = grupo.Id,
            LocationId = ubs.Id,
            Shift = Turnos.Manha,
            PeriodLabel = "07/09 a 18/09",
            StartDate = new DateOnly(2026, 9, 7),
            EndDate = new DateOnly(2026, 9, 18),
            ActivityType = "assistencia",
            RequiredHours = 80
        };

        db.AddRange(aluno, grupo, ubs, faculdade, escala);
        db.Add(new GroupMembership { StudentId = aluno.Id, GroupId = grupo.Id });
        await db.SaveChangesAsync();

        return new Cenario(db, aluno, grupo, escala, ubs, faculdade);
    }

    private static ProgramacaoService Servico(AppDbContext db) => new(db);

    // ── Rodízio sem programação semanal ───────────────────────────────────────
    [Fact]
    public async Task Sem_programacao_semanal_dia_util_e_presencial_no_local_principal()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        var dia = await Servico(c.Db).ObterAsync(c.Aluno.Id, Segunda, Turnos.Manha);

        Assert.Equal(ModoAtividade.Presencial, dia.Modo);
        Assert.Equal(ValidacaoPresenca.Localizacao, dia.Validacao);
        Assert.Equal(c.Ubs.Id, dia.Local?.Id);
        Assert.True(dia.ExigePonto);
    }

    [Fact]
    public async Task Fim_de_semana_nao_gera_obrigacao_de_ponto()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        var dia = await Servico(c.Db).ObterAsync(c.Aluno.Id, Domingo, Turnos.Manha);

        Assert.Equal(ModoAtividade.SemAtividade, dia.Modo);
        Assert.False(dia.ExigePonto);
    }

    // ── Programação por dia da semana ─────────────────────────────────────────
    [Fact]
    public async Task Sexta_programada_na_faculdade_muda_o_local_do_dia()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        c.Db.Add(new RotationDaySchedule
        {
            ScheduleId = c.Escala.Id,
            DayOfWeek = (int)DayOfWeek.Friday,
            Mode = ModoAtividade.Presencial,
            LocationId = c.Faculdade.Id
        });
        await c.Db.SaveChangesAsync();

        var sexta = await Servico(c.Db).ObterAsync(c.Aluno.Id, Sexta, Turnos.Manha);
        var segunda = await Servico(c.Db).ObterAsync(c.Aluno.Id, Segunda, Turnos.Manha);

        Assert.Equal(c.Faculdade.Id, sexta.Local?.Id);
        // A segunda continua sem regra própria: herda o local principal do rodízio.
        Assert.Equal(c.Ubs.Id, segunda.Local?.Id);
    }

    [Fact]
    public async Task Dia_programado_como_remoto_e_validado_por_codigo()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        c.Db.Add(new RotationDaySchedule
        {
            ScheduleId = c.Escala.Id,
            DayOfWeek = (int)DayOfWeek.Friday,
            Mode = ModoAtividade.Remoto
        });
        await c.Db.SaveChangesAsync();

        var dia = await Servico(c.Db).ObterAsync(c.Aluno.Id, Sexta, Turnos.Manha);

        Assert.Equal(ModoAtividade.Remoto, dia.Modo);
        Assert.Equal(ValidacaoPresenca.Codigo, dia.Validacao);
        Assert.Null(dia.Local);
        Assert.True(dia.ExigePonto);
    }

    [Fact]
    public async Task Dia_remoto_lista_as_atividades_do_grupo_naquela_data()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        var professor = TestSupport.Usuario(Roles.Supervisor);
        c.Db.Add(professor);
        c.Db.Add(new RotationDaySchedule
        {
            ScheduleId = c.Escala.Id,
            DayOfWeek = (int)DayOfWeek.Friday,
            Mode = ModoAtividade.Remoto
        });
        c.Db.Add(new RemoteActivity
        {
            Title = "Estudo dirigido sobre segurança do paciente",
            GroupId = c.Grupo.Id,
            ProfessorId = professor.Id,
            ActivityDate = Sexta,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            EstimatedHours = 4,
            PresenceCode = "ENF-7K92"
        });
        await c.Db.SaveChangesAsync();

        var dia = await Servico(c.Db).ObterAsync(c.Aluno.Id, Sexta, Turnos.Manha);

        Assert.Single(dia.AtividadesRemotas);
        Assert.Empty(dia.AtividadesConcluidas);
    }

    // ── Exceções do calendário ────────────────────────────────────────────────
    [Fact]
    public async Task Feriado_da_faculdade_dispensa_o_ponto()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        c.Db.Add(new CalendarException
        {
            Type = TipoExcecao.Feriado,
            Scope = AbrangenciaExcecao.Faculdade,
            StartDate = Sexta,
            EndDate = Sexta,
            Description = "Feriado municipal"
        });
        await c.Db.SaveChangesAsync();

        var dia = await Servico(c.Db).ObterAsync(c.Aluno.Id, Sexta, Turnos.Manha);

        Assert.Equal(ModoAtividade.SemAtividade, dia.Modo);
        Assert.False(dia.ExigePonto);
        Assert.Contains("Feriado municipal", dia.Motivo);
    }

    [Fact]
    public async Task Troca_de_local_redireciona_o_geofence_do_dia()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        c.Db.Add(new CalendarException
        {
            Type = TipoExcecao.TrocaLocal,
            Scope = AbrangenciaExcecao.Turma,
            GroupId = c.Grupo.Id,
            StartDate = Segunda,
            EndDate = Segunda,
            LocationId = c.Faculdade.Id,
            Description = "UBS em manutenção"
        });
        await c.Db.SaveChangesAsync();

        var dia = await Servico(c.Db).ObterAsync(c.Aluno.Id, Segunda, Turnos.Manha);

        Assert.Equal(ModoAtividade.Presencial, dia.Modo);
        Assert.Equal(c.Faculdade.Id, dia.Local?.Id);
    }

    [Fact]
    public async Task Excecao_remota_substitui_a_validacao_por_localizacao()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        c.Db.Add(new CalendarException
        {
            Type = TipoExcecao.Remoto,
            Scope = AbrangenciaExcecao.Rodizio,
            ScheduleId = c.Escala.Id,
            StartDate = Segunda,
            EndDate = Segunda,
            Description = "Unidade indisponível"
        });
        await c.Db.SaveChangesAsync();

        var dia = await Servico(c.Db).ObterAsync(c.Aluno.Id, Segunda, Turnos.Manha);

        Assert.Equal(ValidacaoPresenca.Codigo, dia.Validacao);
        Assert.Null(dia.Local);
    }

    [Fact]
    public async Task Excecao_do_aluno_vence_a_da_faculdade()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        c.Db.Add(new CalendarException
        {
            Type = TipoExcecao.Feriado,
            Scope = AbrangenciaExcecao.Faculdade,
            StartDate = Segunda,
            EndDate = Segunda,
            Description = "Feriado"
        });
        c.Db.Add(new CalendarException
        {
            Type = TipoExcecao.Reposicao,
            Scope = AbrangenciaExcecao.Aluno,
            StudentId = c.Aluno.Id,
            StartDate = Segunda,
            EndDate = Segunda,
            Description = "Reposição individual"
        });
        await c.Db.SaveChangesAsync();

        var dia = await Servico(c.Db).ObterAsync(c.Aluno.Id, Segunda, Turnos.Manha);

        // A reposição não dispensa o ponto: o aluno continua devendo o dia.
        Assert.Equal(ModoAtividade.Presencial, dia.Modo);
        Assert.Equal(TipoExcecao.Reposicao, dia.TipoExcecao);
    }

    [Fact]
    public async Task Excecao_de_curso_alcanca_apenas_o_curso_informado()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        c.Db.Add(new CalendarException
        {
            Type = TipoExcecao.Recesso,
            Scope = AbrangenciaExcecao.Curso,
            Course = "Medicina",
            StartDate = Segunda,
            EndDate = Segunda,
            Description = "Recesso do curso de Medicina"
        });
        await c.Db.SaveChangesAsync();

        var dia = await Servico(c.Db).ObterAsync(c.Aluno.Id, Segunda, Turnos.Manha);

        Assert.Equal(ModoAtividade.Presencial, dia.Modo);
        Assert.Null(dia.ExcecaoId);
    }

    [Fact]
    public async Task Excecao_de_um_turno_nao_afeta_o_outro()
    {
        var c = await MontarAsync();
        using var _ = c.Db;

        c.Db.Add(new CalendarException
        {
            Type = TipoExcecao.Cancelado,
            Scope = AbrangenciaExcecao.Turma,
            GroupId = c.Grupo.Id,
            Shift = Turnos.Tarde,
            StartDate = Segunda,
            EndDate = Segunda,
            Description = "Tarde cancelada"
        });
        await c.Db.SaveChangesAsync();

        var manha = await Servico(c.Db).ObterAsync(c.Aluno.Id, Segunda, Turnos.Manha);

        Assert.Equal(ModoAtividade.Presencial, manha.Modo);
    }

    // ── Aluno sem rodízio ─────────────────────────────────────────────────────
    [Fact]
    public async Task Aluno_sem_rodizio_fica_sem_programacao()
    {
        var db = TestSupport.NovoContexto();
        using var _ = db;

        var aluno = TestSupport.Aluno();
        db.Add(aluno);
        await db.SaveChangesAsync();

        var dia = await Servico(db).ObterAsync(aluno.Id, Segunda, Turnos.Manha);

        Assert.Equal(ModoAtividade.SemAtividade, dia.Modo);
        Assert.Null(dia.ScheduleId);
        Assert.Null(dia.ExcecaoId);
    }
}
