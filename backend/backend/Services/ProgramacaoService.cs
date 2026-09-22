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
    internal sealed record Contexto(
        ApplicationUser? Aluno,
        /// <summary>Todas as turmas do aluno: ele pode cursar mais de um rodízio ao mesmo tempo.</summary>
        List<Guid> GroupIds,
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

    private async Task<Contexto> CarregarAsync(Guid studentId, DateOnly de, DateOnly ate, CancellationToken ct) =>
        (await CarregarContextosAsync([studentId], de, ate, ct)).Contextos[studentId];

    /// <summary>
    /// Programação de vários alunos num intervalo, lida em cinco consultas no
    /// total — em vez de cinco por aluno. É o que permite ao painel do professor
    /// saber quem deveria estar em estágio em cada dia sem abrir centenas de
    /// consultas. A resolução de cada dia é a mesma do check-in.
    /// </summary>
    public async Task<ProgramacaoLote> CarregarLoteAsync(
        IReadOnlyCollection<Guid> studentIds, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var (contextos, entradas) = await CarregarContextosAsync(studentIds, de, ate, ct);
        return new ProgramacaoLote(contextos, entradas);
    }

    /// <summary>Programação já carregada de um grupo de alunos, resolvida em memória.</summary>
    public sealed class ProgramacaoLote
    {
        private readonly Dictionary<Guid, Contexto> _contextos;
        private readonly Dictionary<(Guid Aluno, Guid Turma), DateOnly> _entradas;

        internal ProgramacaoLote(Dictionary<Guid, Contexto> contextos, Dictionary<(Guid, Guid), DateOnly> entradas)
        {
            _contextos = contextos;
            _entradas = entradas;
        }

        /// <summary>
        /// O dia do aluno naquele turno, ou <c>null</c> quando ele não tem rodízio
        /// no turno — ou ainda não estava na turma do rodízio naquela data (a
        /// turma não cobra presença de antes de o aluno entrar nela).
        /// </summary>
        public ProgramacaoDia? NoTurno(Guid studentId, DateOnly data, string turno)
        {
            if (!_contextos.TryGetValue(studentId, out var ctx)) return null;

            var escala = ctx.Escalas.FirstOrDefault(s =>
                s.StartDate <= data && s.EndDate >= data && Turnos.Normalizar(s.Shift) == turno);
            if (escala == null) return null;

            if (_entradas.TryGetValue((studentId, escala.GroupId), out var entrada) && data < entrada)
                return null;

            return Resolver(ctx, data, turno);
        }
    }

    private async Task<(Dictionary<Guid, Contexto> Contextos, Dictionary<(Guid, Guid), DateOnly> Entradas)>
        CarregarContextosAsync(IReadOnlyCollection<Guid> studentIds, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var alunos = await db.Users.AsNoTracking()
            .Where(u => studentIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var vinculos = await db.GroupMemberships.AsNoTracking()
            .Where(m => studentIds.Contains(m.StudentId))
            .Select(m => new { m.StudentId, m.GroupId, m.CreatedAt })
            .ToListAsync(ct);

        var groupIds = vinculos.Select(v => v.GroupId).Distinct().ToList();

        // Cursando dois rodízios ao mesmo tempo, o dia do aluno sai da união das
        // escalas das suas turmas; quem separa uma da outra é o turno.
        List<RotationSchedule> escalas = groupIds.Count == 0
            ? []
            : await db.RotationSchedules
                .AsNoTracking()
                .Include(s => s.Location)
                .Include(s => s.Days).ThenInclude(d => d.Location)
                .Where(s => groupIds.Contains(s.GroupId) && s.StartDate <= ate && s.EndDate >= de)
                .ToListAsync(ct);

        var excecoes = await db.CalendarExceptions
            .AsNoTracking()
            .Include(x => x.Location)
            .Where(x => x.StartDate <= ate && x.EndDate >= de)
            .ToListAsync(ct);

        List<RemoteActivity> atividades = groupIds.Count == 0
            ? []
            : await db.RemoteActivities
                .AsNoTracking()
                .Where(a => a.Ativo && groupIds.Contains(a.GroupId)
                         && a.ActivityDate >= de && a.ActivityDate <= ate)
                .OrderBy(a => a.StartTime)
                .ToListAsync(ct);

        var participacoes = (await db.RemoteActivityParticipations
                .AsNoTracking()
                .Where(p => studentIds.Contains(p.StudentId)
                         && p.RemoteActivity.ActivityDate >= de && p.RemoteActivity.ActivityDate <= ate)
                .Select(p => new { p.StudentId, p.RemoteActivityId })
                .ToListAsync(ct))
            .GroupBy(p => p.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.RemoteActivityId).ToHashSet());

        var turmasPorAluno = vinculos
            .GroupBy(v => v.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(v => v.GroupId).Distinct().ToList());

        var contextos = new Dictionary<Guid, Contexto>();
        foreach (var id in studentIds.Distinct())
        {
            var turmas = turmasPorAluno.GetValueOrDefault(id) ?? [];
            contextos[id] = new Contexto(
                alunos.GetValueOrDefault(id),
                turmas,
                [.. escalas.Where(s => turmas.Contains(s.GroupId))],
                excecoes,
                [.. atividades.Where(a => turmas.Contains(a.GroupId))],
                participacoes.GetValueOrDefault(id) ?? []);
        }

        // O vínculo é gravado em UTC; o dia do estágio é o de Brasília.
        var entradas = vinculos
            .GroupBy(v => (v.StudentId, v.GroupId))
            .ToDictionary(g => g.Key, g => DateOnly.FromDateTime(BrasiliaTime.DeUtc(g.Min(v => v.CreatedAt))));

        return (contextos, entradas);
    }

    private static ProgramacaoDia Resolver(Contexto ctx, DateOnly data, string? turno)
    {
        var escalas = ctx.Escalas.Where(s => s.StartDate <= data && s.EndDate >= data).ToList();

        var turnoAlvo = Turnos.Normalizar(turno)
                     ?? Turnos.Normalizar(escalas.Count == 1 ? escalas[0].Shift : null)
                     ?? Turnos.DaHora(BrasiliaTime.Agora);

        // Com o turno pedido explicitamente, só vale a escala daquele turno: antes a
        // única escala do dia respondia por qualquer turno, e quem tinha rodízio só à
        // noite aparecia com programação também de manhã (reposição de sábado).
        var escala = escalas.FirstOrDefault(s => Turnos.Normalizar(s.Shift) == turnoAlvo)
                  ?? (Turnos.Normalizar(turno) == null && escalas.Count == 1 ? escalas[0] : null);

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
    /// abrangência mais específica (aluno, rodízio, turma, faculdade) e,
    /// no empate, a cadastrada por último.
    /// </summary>
    private static CalendarException? ExcecaoAplicavel(
        Contexto ctx, Guid? scheduleId, DateOnly data, string turno) =>
        ctx.Excecoes
            .Where(x => x.AlcancaData(data))
            .Where(x => x.Shift == null || Turnos.Normalizar(x.Shift) == turno)
            .Where(x => Alcanca(x, ctx.Aluno, ctx.GroupIds, scheduleId))
            .OrderByDescending(x => Models.AbrangenciaExcecao.Especificidade(x.Scope))
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();

    private static bool Alcanca(CalendarException x, ApplicationUser? aluno, List<Guid> groupIds, Guid? scheduleId)
        => x.Scope switch
        {
            Models.AbrangenciaExcecao.Aluno => aluno != null && x.StudentId == aluno.Id,
            Models.AbrangenciaExcecao.Rodizio => scheduleId != null && x.ScheduleId == scheduleId,
            Models.AbrangenciaExcecao.Turma => x.GroupId != null && groupIds.Contains(x.GroupId.Value),
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
