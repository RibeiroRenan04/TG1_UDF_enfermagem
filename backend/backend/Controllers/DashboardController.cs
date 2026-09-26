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
public class DashboardController(
    AppDbContext db, PendenciasService pendenciasService, PainelGestaoService painelGestao,
    EscopoPreceptorService escopoPreceptor) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;

        // O preceptor vê só os pontos dos rodízios que supervisiona.
        var escopo = role == Roles.Preceptor ? await escopoPreceptor.CarregarAsync(userId) : null;

        var query = db.AttendanceRecords.AsQueryable();
        if (role == Roles.Aluno)
            query = query.Where(r => r.StudentId == userId);
        else if (escopo != null)
            query = EscopoPreceptorService.FiltrarPontos(query, escopo);

        // A gestão não vê horas no painel: para ela basta contar por situação no próprio banco, em vez
        // de trazer o ponto da faculdade inteira (dezenas de milhares de linhas) para somar em memória.
        var ehGestao = role is Roles.Supervisor or Roles.Secretaria;
        var recs = ehGestao
            ? []
            : await query.AsNoTracking()
                .Select(r => new { r.Type, r.Status, r.RecordedAt, r.StudentId })
                .ToListAsync();

        var porStatus = ehGestao
            ? await query.GroupBy(r => r.Status).Select(g => new { Status = g.Key, Total = g.Count() }).ToListAsync()
            : [.. recs.GroupBy(r => r.Status).Select(g => new { Status = g.Key, Total = g.Count() })];

        int ContarPontos(string status) => porStatus.FirstOrDefault(s => s.Status == status)?.Total ?? 0;
        var total = porStatus.Sum(s => s.Total);
        var approved = ContarPontos("aprovado");
        var irregular = ContarPontos("irregular");
        var pending = ContarPontos("pendente");

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
        string? groupCode = null, groupName = null, shift = null;
        var turmas = new List<UserGroupDto>();

        if (role == Roles.Aluno)
        {
            // Com mais de uma turma, a carga exigida é a soma dos rodízios.
            var vinculos = await db.GroupMemberships
                .Include(m => m.Group).ThenInclude(g => g.Schedules)
                .Where(m => m.StudentId == userId)
                .ToListAsync();

            if (vinculos.Count > 0)
            {
                required = vinculos.Sum(m => m.Group.Schedules.Sum(s => s.RequiredHours));

                var ordenados = TurmasDoAluno.Vigentes(vinculos, BrasiliaTime.Hoje);
                groupCode = TurmasDoAluno.Codigos(ordenados);
                groupName = string.Join(", ", ordenados.Select(m => m.Group.Name));
                turmas = [.. ordenados.Select(m => new UserGroupDto
                {
                    Id = m.GroupId, Code = m.Group.Code, Name = m.Group.Name,
                    Shift = TurmasDoAluno.Turno(m.Group)
                })];
            }

            shift = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Shift)
                .FirstOrDefaultAsync();

            pendencies = await pendenciasService.CalcularAsync(userId);
        }

        var totalStudents = role == Roles.Aluno
            ? 0
            : escopo != null
                ? await db.Users.CountAsync(u => escopo.Alunos.Contains(u.Id) && u.IsActive)
                : await db.Users.CountAsync(u => u.Role == Roles.Aluno && u.IsActive);

        var (ocorrencias, contagens) = await CarregarOcorrenciasAsync(userId, role, escopo);

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
            PendingStatuses = MontarStatusPendentes(role, pendencies, ocorrencias, contagens),
            GroupCode = groupCode,
            GroupName = groupName,
            Shift = shift,
            Groups = turmas
        });
    }

    [HttpGet("gestao")]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<PainelGestaoDto>> GetPainelGestao(CancellationToken ct) =>
        Ok(await painelGestao.MontarAsync(ct));

    /// <summary>Endpoint próprio: a tela recarrega o card após registrar uma irregularidade.</summary>
    [HttpGet("status-pendentes")]
    public async Task<ActionResult<List<PendingStatusDto>>> GetPendingStatuses()
    {
        var userId = UsuarioAtual();
        var role = PapelAtual();

        List<PendencyDto> pendencies = role == Roles.Aluno
            ? await pendenciasService.CalcularAsync(userId)
            : [];
        var (ocorrencias, contagens) = await CarregarOcorrenciasAsync(userId, role);

        return Ok(MontarStatusPendentes(role, pendencies, ocorrencias, contagens));
    }

    /// <summary>
    /// Só o aluno precisa das ocorrências em si (prazo e motivo da negada); preceptor e gestão só usam as
    /// contagens, que saem agrupadas do banco sem trazer as linhas.
    /// </summary>
    private async Task<(List<PointIrregularity> Ocorrencias, IrregularityCountsDto Contagens)> CarregarOcorrenciasAsync(
        Guid userId, string role, EscopoPreceptorService.Escopo? escopo = null)
    {
        var query = db.PointIrregularities.AsNoTracking();

        if (role == Roles.Aluno)
        {
            var minhas = await query.Where(i => i.StudentId == userId)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
            return (minhas, ContarIrregularidades(minhas.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count())));
        }

        if (role == Roles.Preceptor)
            query = EscopoPreceptorService.FiltrarOcorrencias(query, escopo ?? await escopoPreceptor.CarregarAsync(userId));

        var porStatus = await query
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Total);
        return ([], ContarIrregularidades(porStatus));
    }

    private static IrregularityCountsDto ContarIrregularidades(Dictionary<string, int> porStatus)
    {
        int Contar(string status) => porStatus.GetValueOrDefault(status);

        var aguardandoPreceptor = Contar(PointIrregularity.StatusAguardandoPreceptor);
        var aguardandoProfessor = Contar(PointIrregularity.StatusAguardandoProfessor);

        return new IrregularityCountsDto
        {
            AwaitingPreceptor = aguardandoPreceptor,
            AwaitingProfessor = aguardandoProfessor,
            Approved = Contar(PointIrregularity.StatusAprovada),
            Denied = Contar(PointIrregularity.StatusNegada),
            Open = aguardandoPreceptor + aguardandoProfessor,
            Total = porStatus.Values.Sum()
        };
    }

    /// <summary>Aluno: prazos e contestações; preceptor e professor: o que espera uma ação deles.</summary>
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
        else if ((role is Roles.Supervisor or Roles.Secretaria) && contagens.AwaitingProfessor > 0)
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
        return Ok(await pendenciasService.CalcularAsync(targetId));
    }
}
