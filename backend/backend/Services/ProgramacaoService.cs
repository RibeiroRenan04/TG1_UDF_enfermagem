using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>
/// Responde à pergunta que passou a comandar o ponto: <b>onde o aluno deveria estar
/// e qual atividade deveria realizar naquele dia?</b>
///
/// O registro de ponto deixa de ser um evento isolado e passa a ser consequência da
/// programação: é ela que diz se o dia é presencial (validação por localização),
/// remoto (validação por código) ou sem atividade (nenhuma obrigação de ponto).
///
/// A programação de uma data sai de três camadas, nesta ordem:
/// 1. o rodízio do grupo no período e turno;
/// 2. a regra do dia da semana daquele rodízio (segunda→UBS, sexta→faculdade…);
/// 3. as exceções do calendário, da mais específica para a mais genérica.
/// </summary>
public class ProgramacaoService(AppDbContext db)
{
    /// <summary>Programação de um dia para um aluno.</summary>
    public record ProgramacaoDia
    {
        public required DateOnly Data { get; init; }
        public required string Turno { get; init; }

        /// <summary>"presencial" | "remoto" | "sem_atividade".</summary>
        public required string Modo { get; init; }

        /// <summary>"localizacao" | "codigo" | "nenhuma".</summary>
        public required string Validacao { get; init; }

        /// <summary>Há ponto a registrar neste dia.</summary>
        public bool ExigePonto => Modo != ModoAtividade.SemAtividade;

        public Guid? ScheduleId { get; init; }
        public string? PeriodLabel { get; init; }
        public string? ActivityType { get; init; }
        public Location? Local { get; init; }

        /// <summary>Por que o dia ficou assim ("Feriado: Independência", "Fim de semana").</summary>
        public string? Motivo { get; init; }

        // ── Exceção aplicada, quando houver ───────────────────────────────────
        public Guid? ExcecaoId { get; init; }
        public string? TipoExcecao { get; init; }
        public string? AbrangenciaExcecao { get; init; }

        /// <summary>Atividades remotas do dia em que o aluno pode registrar presença.</summary>
        public List<RemoteActivity> AtividadesRemotas { get; init; } = [];

        /// <summary>Atividades remotas do dia em que ele já registrou participação.</summary>
        public List<Guid> AtividadesConcluidas { get; init; } = [];
    }

    /// <summary>
    /// Tudo o que a resolução de um intervalo precisa, lido de uma vez só. As
    /// pendências percorrem um semestre inteiro dia a dia; carregar por dia
    /// transformaria uma abertura do painel em centenas de consultas.
    /// </summary>
    private sealed record Contexto(
        ApplicationUser? Aluno,
        Guid? GroupId,
        List<RotationSchedule> Escalas,
        List<CalendarException> Excecoes,
        List<RemoteActivity> Atividades,
        HashSet<Guid> Participacoes);

    /// <summary>
    /// Programação do aluno na data e turno pedidos. Turno nulo usa o do rodízio do
    /// dia e, na falta dele, o turno correspondente ao horário atual.
    /// </summary>
    public async Task<ProgramacaoDia> ObterAsync(Guid studentId, DateOnly data, string? turno = null,
        CancellationToken ct = default)
    {
        var contexto = await CarregarAsync(studentId, data, data, ct);
        return Resolver(contexto, data, turno);
    }

    /// <summary>
    /// Programação de cada dia de um intervalo — base do calendário do aluno e do
    /// cálculo de pendências.
    /// </summary>
    public async Task<List<ProgramacaoDia>> ObterIntervaloAsync(Guid studentId, DateOnly de, DateOnly ate,
        string? turno = null, CancellationToken ct = default)
    {
        var contexto = await CarregarAsync(studentId, de, ate, ct);

        var dias = new List<ProgramacaoDia>();
        for (var data = de; data <= ate; data = data.AddDays(1))
            dias.Add(Resolver(contexto, data, turno));
        return dias;
    }

    private async Task<Contexto> CarregarAsync(Guid studentId, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var aluno = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId, ct);
        var groupId = await db.GroupMemberships.AsNoTracking()
            .Where(m => m.StudentId == studentId)
            .Select(m => (Guid?)m.GroupId)
            .FirstOrDefaultAsync(ct);

        List<RotationSchedule> escalas = groupId == null
            ? []
            : await db.RotationSchedules
                .AsNoTracking()
                .Include(s => s.Location)
                .Include(s => s.Days).ThenInclude(d => d.Location)
                .Where(s => s.GroupId == groupId && s.StartDate <= ate && s.EndDate >= de)
                .ToListAsync(ct);

        var excecoes = await db.CalendarExceptions
            .AsNoTracking()
            .Include(x => x.Location)
            .Where(x => x.StartDate <= ate && x.EndDate >= de)
            .ToListAsync(ct);

        List<RemoteActivity> atividades = groupId == null
            ? []
            : await db.RemoteActivities
                .AsNoTracking()
                .Where(a => a.Ativo && a.GroupId == groupId
                         && a.ActivityDate >= de && a.ActivityDate <= ate)
                .OrderBy(a => a.StartTime)
                .ToListAsync(ct);

        var participacoes = await db.RemoteActivityParticipations
            .AsNoTracking()
            .Where(p => p.StudentId == studentId
                     && p.RemoteActivity.ActivityDate >= de && p.RemoteActivity.ActivityDate <= ate)
            .Select(p => p.RemoteActivityId)
            .ToListAsync(ct);

        return new Contexto(aluno, groupId, escalas, excecoes, atividades, [.. participacoes]);
    }

    private static ProgramacaoDia Resolver(Contexto ctx, DateOnly data, string? turno)
    {
        var escalas = ctx.Escalas.Where(s => s.StartDate <= data && s.EndDate >= data).ToList();

        var turnoAlvo = Turnos.Normalizar(turno)
                     ?? Turnos.Normalizar(escalas.Count == 1 ? escalas[0].Shift : null)
                     ?? Turnos.DaHora(BrasiliaTime.Agora);

        var escala = escalas.FirstOrDefault(s => Turnos.Normalizar(s.Shift) == turnoAlvo)
                  ?? (escalas.Count == 1 ? escalas[0] : null);

        var baseDia = MontarBase(escala, data, turnoAlvo);

        var excecao = ExcecaoAplicavel(ctx, escala?.Id, data, turnoAlvo);
        var dia = excecao == null ? baseDia : Aplicar(baseDia, excecao);

        if (dia.Modo != ModoAtividade.Remoto) return dia;

        var doDia = ctx.Atividades
            .Where(a => a.ActivityDate == data
                     && (a.ScheduleId == null || a.ScheduleId == escala?.Id))
            .ToList();

        return dia with
        {
            AtividadesRemotas = doDia,
            AtividadesConcluidas = [.. doDia.Where(a => ctx.Participacoes.Contains(a.Id)).Select(a => a.Id)]
        };
    }

    // ── 1 e 2: rodízio e regra do dia da semana ───────────────────────────────
    private static ProgramacaoDia MontarBase(RotationSchedule? escala, DateOnly data, string turno)
    {
        var fimDeSemana = data.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        if (escala == null)
        {
            return new ProgramacaoDia
            {
                Data = data,
                Turno = turno,
                Modo = ModoAtividade.SemAtividade,
                Validacao = ValidacaoPresenca.Nenhuma,
                Motivo = "Nenhum rodízio programado para esta data."
            };
        }

        var regra = escala.Days.FirstOrDefault(d => d.DayOfWeek == (int)data.DayOfWeek);

        // Sem regra cadastrada o rodízio vale como antes: dia útil presencial no
        // local principal, fim de semana sem atividade.
        var modo = regra != null
            ? ModoAtividade.Normalizar(regra.Mode) ?? ModoAtividade.Presencial
            : fimDeSemana ? ModoAtividade.SemAtividade : ModoAtividade.Presencial;

        var local = modo == ModoAtividade.Presencial
            ? regra?.Location ?? escala.Location
            : null;

        return new ProgramacaoDia
        {
            Data = data,
            Turno = turno,
            Modo = modo,
            Validacao = ValidacaoPresenca.De(modo),
            ScheduleId = escala.Id,
            PeriodLabel = escala.PeriodLabel,
            ActivityType = escala.ActivityType,
            Local = local,
            Motivo = regra?.Notes ?? (regra == null && fimDeSemana ? "Fim de semana." : null)
        };
    }

    // ── 3: exceções do calendário ─────────────────────────────────────────────
    /// <summary>
    /// Exceção que vale para o aluno naquela data. Havendo mais de uma, vence a de
    /// abrangência mais específica (aluno, rodízio, turma, curso, faculdade) e,
    /// no empate, a cadastrada por último.
    /// </summary>
    private static CalendarException? ExcecaoAplicavel(
        Contexto ctx, Guid? scheduleId, DateOnly data, string turno) =>
        ctx.Excecoes
            .Where(x => x.AlcancaData(data))
            .Where(x => x.Shift == null || Turnos.Normalizar(x.Shift) == turno)
            .Where(x => Alcanca(x, ctx.Aluno, ctx.GroupId, scheduleId))
            .OrderByDescending(x => Models.AbrangenciaExcecao.Especificidade(x.Scope))
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();

    private static bool Alcanca(CalendarException x, ApplicationUser? aluno, Guid? groupId, Guid? scheduleId)
        => x.Scope switch
        {
            Models.AbrangenciaExcecao.Aluno => aluno != null && x.StudentId == aluno.Id,
            Models.AbrangenciaExcecao.Rodizio => scheduleId != null && x.ScheduleId == scheduleId,
            Models.AbrangenciaExcecao.Turma => groupId != null && x.GroupId == groupId,
            Models.AbrangenciaExcecao.Curso => !string.IsNullOrWhiteSpace(x.Course)
                && string.Equals(x.Course, aluno?.Course, StringComparison.OrdinalIgnoreCase),
            Models.AbrangenciaExcecao.Faculdade => true,
            _ => false
        };

    private static ProgramacaoDia Aplicar(ProgramacaoDia dia, CalendarException x)
    {
        var rotulo = Models.TipoExcecao.Rotulo(x.Type);
        var motivo = string.IsNullOrWhiteSpace(x.Description) ? rotulo : $"{rotulo}: {x.Description}";

        var marcado = dia with
        {
            ExcecaoId = x.Id,
            TipoExcecao = x.Type,
            AbrangenciaExcecao = x.Scope,
            Motivo = motivo
        };

        if (Models.TipoExcecao.DispensaPonto(x.Type))
            return marcado with
            {
                Modo = ModoAtividade.SemAtividade,
                Validacao = ValidacaoPresenca.Nenhuma,
                Local = null
            };

        if (x.Type == Models.TipoExcecao.Remoto)
            return marcado with
            {
                Modo = ModoAtividade.Remoto,
                Validacao = ValidacaoPresenca.Codigo,
                Local = null
            };

        // A unidade da exceção vem carregada com ela (Include), então a troca de
        // local não custa uma consulta a mais por dia.
        if (x.Type == Models.TipoExcecao.TrocaLocal)
            return marcado with
            {
                Modo = ModoAtividade.Presencial,
                Validacao = ValidacaoPresenca.Localizacao,
                Local = x.Location ?? dia.Local
            };

        // Atividade especial e reposição não mudam a forma de validar: registram o
        // motivo do dia e, quando informado, o local alternativo.
        if (x.Location != null && dia.Modo == ModoAtividade.Presencial)
            return marcado with { Local = x.Location };

        return marcado;
    }
}
