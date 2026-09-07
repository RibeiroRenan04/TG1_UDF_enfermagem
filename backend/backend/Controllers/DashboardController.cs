using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using EstagioCheck.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController(AppDbContext db, ProgramacaoService programacao) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;

        var query = db.AttendanceRecords.AsQueryable();
        if (role == Roles.Aluno)
            query = query.Where(r => r.StudentId == userId);

        var recs = await query
            .Select(r => new { r.Type, r.Status, r.RecordedAt, r.StudentId })
            .ToListAsync();

        var total = recs.Count;
        var approved = recs.Count(r => r.Status == "aprovado");
        var irregular = recs.Count(r => r.Status == "irregular");
        var pending = recs.Count(r => r.Status == "pendente");

        // Calcula horas (par check_in/check_out por aluno+dia)
        var byStudentDay = recs
            .GroupBy(r => $"{r.StudentId}|{r.RecordedAt:yyyy-MM-dd}")
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    In = g.Where(r => r.Type == "check_in").Select(r => (DateTime?)r.RecordedAt).FirstOrDefault(),
                    Out = g.Where(r => r.Type == "check_out").Select(r => (DateTime?)r.RecordedAt).FirstOrDefault()
                });

        double hours = 0;
        foreach (var pair in byStudentDay.Values)
            if (pair.In.HasValue && pair.Out.HasValue)
                hours += Math.Max(0, (pair.Out.Value - pair.In.Value).TotalHours);

        int required = 0;
        var pendencies = new List<PendencyDto>();

        if (role == Roles.Aluno)
        {
            var membership = await db.GroupMemberships
                .Include(m => m.Group).ThenInclude(g => g.Schedules)
                .FirstOrDefaultAsync(m => m.StudentId == userId);

            if (membership != null)
                required = membership.Group.Schedules.Sum(s => s.RequiredHours);

            pendencies = await GetStudentPendencies(userId);
        }

        // Contador de alunos do painel de gestão. A resposta não trazia o campo,
        // então a tela do professor exibia zero sempre.
        var totalStudents = role == Roles.Aluno
            ? 0
            : await db.Users.CountAsync(u => u.Role == Roles.Aluno && u.IsActive);

        // As ocorrências vêm da mesma tabela da tela de irregularidades — o que o
        // aluno acabou de enviar já entra nesta contagem.
        var ocorrencias = await CarregarIrregularidadesAsync(userId, role);
        var contagens = ContarIrregularidades(ocorrencias);

        return Ok(new DashboardStatsDto
        {
            Total = total,
            Approved = approved,
            Irregular = irregular,
            Pending = pending,
            Hours = Math.Round(hours, 1),
            Required = required,
            PendencyDays = pendencies.Count,
            PendencyHours = Math.Round(pendencies.Sum(p => p.ExpectedHours), 1),
            Pendencies = pendencies,
            TotalStudents = totalStudents,
            Irregularities = contagens,
            PendingStatuses = MontarStatusPendentes(role, pendencies, ocorrencias, contagens)
        });
    }

    // ── Status pendentes ──────────────────────────────────────────────────────
    /// <summary>
    /// Avisos centralizados do painel. Ficam em um endpoint próprio também porque
    /// a tela recarrega o card após registrar uma irregularidade.
    /// </summary>
    [HttpGet("status-pendentes")]
    public async Task<ActionResult<List<PendingStatusDto>>> GetPendingStatuses()
    {
        var userId = UsuarioAtual();
        var role = PapelAtual();

        List<PendencyDto> pendencies = role == Roles.Aluno
            ? await GetStudentPendencies(userId)
            : [];
        var ocorrencias = await CarregarIrregularidadesAsync(userId, role);

        return Ok(MontarStatusPendentes(role, pendencies, ocorrencias, ContarIrregularidades(ocorrencias)));
    }

    /// <summary>
    /// Ocorrências visíveis ao usuário: o aluno vê as próprias, o preceptor as dos
    /// alunos que supervisiona, professor e coordenadora veem todas.
    /// </summary>
    private async Task<List<PointIrregularity>> CarregarIrregularidadesAsync(Guid userId, string role)
    {
        var query = db.PointIrregularities
            .AsNoTracking()
            .AsQueryable();

        if (role == Roles.Aluno)
        {
            query = query.Where(i => i.StudentId == userId);
        }
        else if (role == Roles.Preceptor)
        {
            var grupoIds = await db.RotationSchedules
                .Where(sc => sc.PreceptorId == userId)
                .Select(sc => sc.GroupId)
                .Distinct()
                .ToListAsync();

            var alunoIds = await db.GroupMemberships
                .Where(m => grupoIds.Contains(m.GroupId))
                .Select(m => m.StudentId)
                .ToListAsync();

            query = query.Where(i => alunoIds.Contains(i.StudentId));
        }

        return await query.OrderByDescending(i => i.CreatedAt).ToListAsync();
    }

    private static IrregularityCountsDto ContarIrregularidades(List<PointIrregularity> ocorrencias)
    {
        int Contar(string status) => ocorrencias.Count(i => i.Status == status);

        var aguardandoPreceptor = Contar(PointIrregularity.StatusAguardandoPreceptor);
        var aguardandoProfessor = Contar(PointIrregularity.StatusAguardandoProfessor);

        return new IrregularityCountsDto
        {
            AwaitingPreceptor = aguardandoPreceptor,
            AwaitingProfessor = aguardandoProfessor,
            Approved = Contar(PointIrregularity.StatusAprovada),
            Denied = Contar(PointIrregularity.StatusNegada),
            Open = aguardandoPreceptor + aguardandoProfessor,
            Total = ocorrencias.Count
        };
    }

    /// <summary>
    /// Monta os avisos do card "Status Pendentes" conforme o perfil: o aluno vê os
    /// próprios prazos e o andamento das contestações; preceptor e professor veem
    /// o que está parado esperando uma ação deles.
    /// </summary>
    private static List<PendingStatusDto> MontarStatusPendentes(
        string role,
        List<PendencyDto> pendencies,
        List<PointIrregularity> ocorrencias,
        IrregularityCountsDto contagens)
    {
        var avisos = new List<PendingStatusDto>();

        if (role == Roles.Aluno)
        {
            if (pendencies.Count > 0)
            {
                var maisRecente = pendencies[0];
                avisos.Add(new PendingStatusDto
                {
                    Kind = "dias_sem_registro",
                    Severity = pendencies.Count > 2 ? "critico" : "atencao",
                    Title = pendencies.Count == 1
                        ? "1 dia de estágio sem registro"
                        : $"{pendencies.Count} dias de estágio sem registro",
                    Detail = $"O mais recente é {maisRecente.PendencyDate:dd/MM/yyyy} em {maisRecente.LocationName}. "
                           + "Se você esteve presente, abra uma irregularidade para análise.",
                    Link = "/app/irregularidades",
                    LinkLabel = "Registrar irregularidade",
                    Count = pendencies.Count,
                    ReferenceDate = maisRecente.PendencyDate.ToDateTime(TimeOnly.MinValue)
                });
            }

            if (contagens.Open > 0)
            {
                var ultima = ocorrencias.First(i => i.Status is PointIrregularity.StatusAguardandoPreceptor
                                                              or PointIrregularity.StatusAguardandoProfessor);
                avisos.Add(new PendingStatusDto
                {
                    Kind = "irregularidade_em_analise",
                    Severity = "info",
                    Title = contagens.Open == 1
                        ? "1 irregularidade em análise"
                        : $"{contagens.Open} irregularidades em análise",
                    Detail = ultima.Status == PointIrregularity.StatusAguardandoPreceptor
                        ? "Aguardando a ciência do preceptor."
                        : "Encaminhada ao professor responsável.",
                    Link = "/app/irregularidades",
                    LinkLabel = "Acompanhar",
                    Count = contagens.Open,
                    ReferenceDate = ultima.CreatedAt
                });
            }

            var negadas = ocorrencias
                .Where(i => i.Status == PointIrregularity.StatusNegada)
                .ToList();
            if (negadas.Count > 0)
            {
                avisos.Add(new PendingStatusDto
                {
                    Kind = "irregularidade_negada",
                    Severity = "critico",
                    Title = negadas.Count == 1
                        ? "1 irregularidade negada"
                        : $"{negadas.Count} irregularidades negadas",
                    Detail = "O professor não acatou a justificativa. Veja o parecer — você pode abrir "
                           + "uma nova contestação para o mesmo ponto.",
                    Link = "/app/irregularidades",
                    LinkLabel = "Ver parecer",
                    Count = negadas.Count,
                    ReferenceDate = negadas[0].ProfessorDecidedAt ?? negadas[0].UpdatedAt
                });
            }
        }
        else if (role == Roles.Preceptor && contagens.AwaitingPreceptor > 0)
        {
            avisos.Add(new PendingStatusDto
            {
                Kind = "aguardando_ciencia",
                Severity = "atencao",
                Title = contagens.AwaitingPreceptor == 1
                    ? "1 irregularidade aguardando sua ciência"
                    : $"{contagens.AwaitingPreceptor} irregularidades aguardando sua ciência",
                Detail = "Registre a ciência e a observação para encaminhar ao professor responsável.",
                Link = "/app/irregularidades",
                LinkLabel = "Analisar",
                Count = contagens.AwaitingPreceptor
            });
        }
        else if ((role is Roles.Supervisor or Roles.Coordenadora) && contagens.AwaitingProfessor > 0)
        {
            avisos.Add(new PendingStatusDto
            {
                Kind = "aguardando_decisao",
                Severity = "atencao",
                Title = contagens.AwaitingProfessor == 1
                    ? "1 irregularidade aguardando decisão"
                    : $"{contagens.AwaitingProfessor} irregularidades aguardando decisão",
                Detail = role == Roles.Supervisor
                    ? "Ocorrências encaminhadas pelo preceptor, à espera do seu parecer."
                    : "Ocorrências encaminhadas ao professor responsável.",
                Link = "/app/irregularidades",
                LinkLabel = "Abrir",
                Count = contagens.AwaitingProfessor
            });
        }

        return avisos;
    }

    private Guid UsuarioAtual() => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    private string PapelAtual() => User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;

    [HttpGet("pendencies")]
    public async Task<ActionResult<List<PendencyDto>>> GetPendencies([FromQuery] Guid? studentId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;

        var targetId = (role == Roles.Aluno) ? userId : (studentId ?? userId);
        var result = await GetStudentPendencies(targetId);
        return Ok(result);
    }

    /// <summary>
    /// Dias passados em que faltou o registro de presença.
    ///
    /// A varredura é dirigida pela programação, não mais pelo calendário civil: só
    /// vira pendência o dia que exigia alguma coisa do aluno. Feriado, recesso,
    /// estágio cancelado e fim de semana simplesmente não entram, e um dia remoto
    /// já cumprido pelo código da atividade também não.
    /// </summary>
    private async Task<List<PendencyDto>> GetStudentPendencies(Guid studentId)
    {
        var today = BrasiliaTime.Hoje;

        var membership = await db.GroupMemberships
            .FirstOrDefaultAsync(m => m.StudentId == studentId);
        if (membership == null) return [];

        var schedules = await db.RotationSchedules
            .Where(s => s.GroupId == membership.GroupId)
            .Select(s => new { s.StartDate, s.EndDate })
            .ToListAsync();
        if (schedules.Count == 0) return [];

        var inicio = schedules.Min(s => s.StartDate);
        var fim = schedules.Max(s => s.EndDate);
        if (fim >= today) fim = today.AddDays(-1);
        if (fim < inicio) return [];

        // Check-ins existentes, indexados por data: o dia remoto gera o mesmo par
        // de registros do presencial, então a comparação vale para os dois.
        var checkInSet = (await db.AttendanceRecords
                .Where(r => r.StudentId == studentId && r.Type == "check_in")
                .Select(r => r.RecordedAt)
                .ToListAsync())
            .Select(DateOnly.FromDateTime)
            .ToHashSet();

        var programacao_ = await programacao.ObterIntervaloAsync(studentId, inicio, fim);

        var pendencies = programacao_
            .Where(dia => dia.ExigePonto && !checkInSet.Contains(dia.Data))
            .Select(dia => new PendencyDto
            {
                PendencyDate = dia.Data,
                ScheduleId = dia.ScheduleId,
                LocationName = dia.Local?.Name ?? ModoAtividade.Rotulo(dia.Modo),
                ExpectedHours = HorasEsperadas(dia)
            });

        return [.. pendencies.OrderByDescending(p => p.PendencyDate)];
    }

    /// <summary>
    /// Carga horária do dia. Vem da janela do turno da unidade; em dia remoto, da
    /// carga informada pelo professor na atividade. Sem nenhuma das duas, 8 horas.
    /// </summary>
    private static double HorasEsperadas(ProgramacaoService.ProgramacaoDia dia)
    {
        if (dia.Local != null
            && TimeSpan.TryParse(dia.Local.ShiftStart, out var inicio)
            && TimeSpan.TryParse(dia.Local.ShiftEnd, out var fim))
            return (fim - inicio).TotalHours;

        if (dia.AtividadesRemotas.Count > 0)
            return dia.AtividadesRemotas.Sum(a => a.EstimatedHours);

        return 8;
    }
}
