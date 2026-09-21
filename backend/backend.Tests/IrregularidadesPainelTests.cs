using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>
/// Indicadores da tela de irregularidades: a fila de decisão, onde ela emperra e
/// os padrões que apontam a causa (tipo, unidade, aluno recorrente).
/// </summary>
public class IrregularidadesPainelTests
{
    private static readonly DateTime Agora = BrasiliaTime.Agora;

    private sealed record Cenario(AppDbContext Db, ApplicationUser Ana, ApplicationUser Bruno,
        ApplicationUser Preceptor, RotationSchedule Escala, Location Ubs);

    private static async Task<Cenario> MontarAsync()
    {
        var db = TestSupport.NovoContexto();
        var ana = TestSupport.Aluno("Ana", "11111111");
        var bruno = TestSupport.Aluno("Bruno", "22222222");
        var preceptor = TestSupport.Usuario(Roles.Preceptor, "Paula Preceptora");
        var ubs = TestSupport.Unidade("UBS Sobradinho");
        var turma = new StudentGroup { Code = "T01", Name = "Turma" };
        var escala = new RotationSchedule
        {
            GroupId = turma.Id, LocationId = ubs.Id, PreceptorId = preceptor.Id, Shift = Turnos.Manha,
            PeriodLabel = "R", StartDate = BrasiliaTime.Hoje.AddDays(-60), EndDate = BrasiliaTime.Hoje.AddDays(30),
            ActivityType = "assistencia", RequiredHours = 80
        };
        db.AddRange(ana, bruno, preceptor, ubs, turma, escala);
        await db.SaveChangesAsync();
        return new Cenario(db, ana, bruno, preceptor, escala, ubs);
    }

    private static PointIrregularity Ocorrencia(Cenario c, ApplicationUser aluno, string tipo, string status,
        int diasAtras, int? diasAteCiencia = null, int? diasAteDecisao = null)
    {
        var criada = Agora.AddDays(-diasAtras);
        return new PointIrregularity
        {
            StudentId = aluno.Id, ScheduleId = c.Escala.Id, Type = tipo, Status = status,
            OccurredOn = DateOnly.FromDateTime(criada), Description = "ocorrência",
            CreatedAt = criada,
            PreceptorAcknowledgedAt = diasAteCiencia.HasValue ? criada.AddDays(diasAteCiencia.Value) : null,
            ProfessorDecidedAt = diasAteDecisao.HasValue ? criada.AddDays(diasAteDecisao.Value) : null
        };
    }

    [Fact]
    public async Task Fila_mostra_quanto_tempo_a_mais_antiga_espera_e_com_qual_preceptor()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        c.Db.AddRange(
            Ocorrencia(c, c.Ana, "atraso", PointIrregularity.StatusAguardandoPreceptor, diasAtras: 9),
            Ocorrencia(c, c.Bruno, "atraso", PointIrregularity.StatusAguardandoPreceptor, diasAtras: 2),
            Ocorrencia(c, c.Ana, "atraso", PointIrregularity.StatusAguardandoProfessor, diasAtras: 10, diasAteCiencia: 4));
        await c.Db.SaveChangesAsync();

        var p = await new IrregularidadesPainelService(c.Db).MontarAsync(30);

        Assert.Equal(2, p.AguardandoPreceptor);
        Assert.Equal(9, p.MaisAntigaAguardandoPreceptorDias);
        Assert.Equal(1, p.AguardandoProfessor);
        // Espera do professor conta da ciência (há 6 dias), não da abertura.
        Assert.Equal(6, p.MaisAntigaAguardandoProfessorDias);

        var fila = Assert.Single(p.FilaPreceptores);
        Assert.Equal("Paula Preceptora", fila.Nome);
        Assert.Equal(2, fila.Pendentes);
        Assert.Equal(9, fila.MaisAntigaDias);
    }

    [Fact]
    public async Task Taxa_de_aprovacao_e_tempos_medios_olham_so_as_decididas_no_periodo()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        c.Db.AddRange(
            Ocorrencia(c, c.Ana, "atraso", PointIrregularity.StatusAprovada, 10, diasAteCiencia: 2, diasAteDecisao: 5),
            Ocorrencia(c, c.Ana, "atraso", PointIrregularity.StatusAprovada, 8, diasAteCiencia: 4, diasAteDecisao: 5),
            Ocorrencia(c, c.Bruno, "outro", PointIrregularity.StatusNegada, 6, diasAteCiencia: 0, diasAteDecisao: 3),
            // Decidida muito antes da janela: fica de fora.
            Ocorrencia(c, c.Bruno, "outro", PointIrregularity.StatusNegada, 200, diasAteCiencia: 1, diasAteDecisao: 2));
        await c.Db.SaveChangesAsync();

        var p = await new IrregularidadesPainelService(c.Db).MontarAsync(30);

        Assert.Equal(3, p.DecididasNoPeriodo);
        Assert.Equal(66.7, p.TaxaAprovacao);
        Assert.Equal(2.0, p.MediaDiasPreceptor);   // (2 + 4 + 0) / 3
        Assert.Equal(2.3, p.MediaDiasProfessor);   // (3 + 1 + 3) / 3
    }

    [Fact]
    public async Task Fora_do_local_concentrado_numa_unidade_aparece_no_ranking_de_unidades()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        c.Db.AddRange(
            Ocorrencia(c, c.Ana, "fora_do_local", PointIrregularity.StatusAguardandoPreceptor, 3),
            Ocorrencia(c, c.Bruno, "fora_do_local", PointIrregularity.StatusAguardandoPreceptor, 2),
            Ocorrencia(c, c.Bruno, "atraso", PointIrregularity.StatusAguardandoPreceptor, 1));
        await c.Db.SaveChangesAsync();

        var p = await new IrregularidadesPainelService(c.Db).MontarAsync(30);

        var unidade = Assert.Single(p.PorUnidade);
        Assert.Equal("UBS Sobradinho", unidade.Nome);
        Assert.Equal(3, unidade.Total);
        Assert.Equal(2, unidade.ForaDoLocal);
        Assert.Equal("fora_do_local", p.PorTipo[0].Tipo);
        Assert.Equal("Registro fora do local", p.PorTipo[0].Rotulo);
    }

    [Fact]
    public async Task Aluno_so_entra_no_ranking_com_ocorrencia_repetida()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        c.Db.AddRange(
            Ocorrencia(c, c.Ana, "atraso", PointIrregularity.StatusNegada, 5, 1, 2),
            Ocorrencia(c, c.Ana, "atraso", PointIrregularity.StatusAguardandoPreceptor, 3),
            Ocorrencia(c, c.Bruno, "atraso", PointIrregularity.StatusAguardandoPreceptor, 3));
        await c.Db.SaveChangesAsync();

        var p = await new IrregularidadesPainelService(c.Db).MontarAsync(30);

        var aluno = Assert.Single(p.PorAluno);
        Assert.Equal("Ana", aluno.Nome);
        Assert.Equal(2, aluno.Total);
        Assert.Equal(1, aluno.Negadas);
    }

    [Fact]
    public async Task Periodo_compara_com_a_janela_anterior_e_agrupa_por_semana()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        c.Db.AddRange(
            Ocorrencia(c, c.Ana, "atraso", PointIrregularity.StatusAguardandoPreceptor, 1),
            Ocorrencia(c, c.Ana, "atraso", PointIrregularity.StatusAguardandoPreceptor, 20),
            Ocorrencia(c, c.Bruno, "atraso", PointIrregularity.StatusAguardandoPreceptor, 40));
        await c.Db.SaveChangesAsync();

        var p = await new IrregularidadesPainelService(c.Db).MontarAsync(30);

        Assert.Equal(2, p.AbertasNoPeriodo);
        Assert.Equal(1, p.AbertasPeriodoAnterior);
        Assert.Equal(2, p.Evolucao.Sum(e => e.Abertas));
        Assert.Equal("semana", p.Agrupamento);
        Assert.InRange(p.Evolucao.Count, 5, 6);   // 30 dias → 5 ou 6 semanas
    }

    [Fact]
    public async Task Historico_inteiro_agrupa_por_mes()
    {
        var c = await MontarAsync();
        using var _ = c.Db;
        c.Db.AddRange(
            Ocorrencia(c, c.Ana, "atraso", PointIrregularity.StatusAguardandoPreceptor, 1),
            Ocorrencia(c, c.Ana, "atraso", PointIrregularity.StatusAguardandoPreceptor, 150));
        await c.Db.SaveChangesAsync();

        var p = await new IrregularidadesPainelService(c.Db).MontarAsync(null);

        Assert.Null(p.AbertasPeriodoAnterior);
        Assert.Equal(2, p.AbertasNoPeriodo);
        Assert.Equal("mes", p.Agrupamento);
        Assert.InRange(p.Evolucao.Count, 5, 7);   // ~5 meses
        Assert.Equal(2, p.Evolucao.Sum(e => e.Abertas));
    }
}
