using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

/// <summary>
/// Acompanhamento formativo. Fluxo de responsabilidades:
///   • Preceptor  → cria, preenche e finaliza o acompanhamento do aluno;
///   • Aluno      → dá ciência do acompanhamento finalizado pelo preceptor;
///   • Professor e coordenadora → consultam somente relatórios já finalizados.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FollowupsController(AppDbContext db, IHttpContextAccessor httpContext) : ControllerBase
{
    private const string StatusRascunho = "rascunho";
    private const string StatusFinalizadoPreceptor = "finalizado_preceptor";
    private const string StatusCienciaAluno = "finalizado_aluno";

    [HttpGet]
    public async Task<ActionResult<List<FollowupDto>>> GetAll()
    {
        var userId = CurrentUserId();
        var role = CurrentRole();

        var query = db.FormativeFollowups
            .Include(f => f.Student)
            .Include(f => f.Preceptor)
            .Include(f => f.Location)
            .AsQueryable();

        query = role switch
        {
            // Aluno vê apenas os próprios documentos já finalizados pelo preceptor.
            Roles.Aluno => query.Where(f => f.StudentId == userId
                && (f.Status == StatusFinalizadoPreceptor || f.Status == StatusCienciaAluno)),

            // Preceptor vê apenas os acompanhamentos que ele mesmo realiza.
            Roles.Preceptor => query.Where(f => f.PreceptorId == userId),

            // Professor e coordenadora recebem somente o relatório finalizado, para consulta.
            Roles.Supervisor or Roles.Coordenadora => query.Where(f => f.Status != StatusRascunho),

            _ => query.Where(_ => false)
        };

        var items = await query.OrderByDescending(f => f.CreatedAt).ToListAsync();
        return Ok(items.Select(Map));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<FollowupDto>> Get(Guid id)
    {
        var userId = CurrentUserId();
        var role = CurrentRole();

        var f = await db.FormativeFollowups
            .Include(x => x.Student).Include(x => x.Preceptor).Include(x => x.Location)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (f == null) return NotFound();
        if (!PodeVisualizar(f, role, userId)) return Forbid();

        return Ok(Map(f));
    }

    /// <summary>
    /// Busca um aluno pelo RGM para preencher o acompanhamento automaticamente.
    /// Além do nome, devolve período/turno/semestre e a escala vigente, evitando
    /// digitação manual e divergência com o que já está cadastrado no sistema.
    /// </summary>
    [HttpGet("student-by-rgm/{rgm}")]
    [Authorize(Roles = Roles.Preceptor)]
    public async Task<ActionResult<StudentLookupDto>> GetStudentByRgm(string rgm)
    {
        var termo = rgm.Trim();

        var student = await db.Users
            .Include(u => u.GroupMembership).ThenInclude(m => m!.Group)
            .FirstOrDefaultAsync(u => u.Role == "aluno" && u.Rgm == termo);

        if (student == null)
            return NotFound(new { message = "Aluno não encontrado para esse RGM." });

        var hoje = BrasiliaTime.Hoje;
        var groupId = student.GroupMembership?.GroupId;

        // Escala do grupo do aluno: prioriza a vigente hoje; senão, a mais recente.
        RotationSchedule? escala = null;
        if (groupId.HasValue)
        {
            var escalas = await db.RotationSchedules
                .Include(s => s.Location)
                .Where(s => s.GroupId == groupId.Value)
                .OrderByDescending(s => s.StartDate)
                .ToListAsync();

            escala = escalas.FirstOrDefault(s => s.StartDate <= hoje && s.EndDate >= hoje)
                  ?? escalas.FirstOrDefault();
        }

        return Ok(new StudentLookupDto
        {
            StudentId = student.Id,
            FullName = student.FullName,
            Rgm = student.Rgm,
            Semester = student.Semester,
            Shift = escala?.Shift ?? student.Shift,
            // Período do rodízio quando houver escala; senão, o semestre do aluno.
            PeriodLabel = escala?.PeriodLabel
                ?? (student.Semester.HasValue ? $"{student.Semester}° semestre" : null),
            GroupId = groupId,
            GroupCode = student.GroupMembership?.Group?.Code,
            GroupName = student.GroupMembership?.Group?.Name,
            ScheduleId = escala?.Id,
            LocationId = escala?.LocationId,
            LocationName = escala?.Location?.Name,
            ActivityType = escala?.ActivityType,
            FollowUpStart = escala?.StartDate,
            FollowUpEnd = escala?.EndDate
        });
    }

    /// <summary>
    /// Rodízios do preceptor com os alunos alocados em cada um. O preceptor
    /// escolhe o aluno pela lista da turma em vez de digitar o RGM de memória;
    /// cada aluno já vem com o contexto do rodízio (período, turno, local e datas)
    /// para preencher o acompanhamento de uma vez.
    /// </summary>
    [HttpGet("my-schedules")]
    [Authorize(Roles = Roles.Preceptor)]
    public async Task<ActionResult<List<ScheduleStudentsDto>>> GetMySchedules()
    {
        var userId = CurrentUserId();
        var hoje = BrasiliaTime.Hoje;

        var escalas = await db.RotationSchedules
            .Include(s => s.Group)
            .Include(s => s.Location)
            .Where(s => s.PreceptorId == userId)
            .OrderByDescending(s => s.StartDate)
            .ToListAsync();

        if (escalas.Count == 0) return Ok(new List<ScheduleStudentsDto>());

        // Uma consulta só para os alunos de todas as turmas envolvidas.
        var grupoIds = escalas.Select(e => e.GroupId).Distinct().ToList();
        var membros = await db.GroupMemberships
            .Include(m => m.Student)
            .Where(m => grupoIds.Contains(m.GroupId) && m.Student.IsActive)
            .OrderBy(m => m.Student.FullName)
            .ToListAsync();

        var resultado = escalas.Select(escala => new ScheduleStudentsDto
        {
            ScheduleId = escala.Id,
            PeriodLabel = escala.PeriodLabel,
            Shift = escala.Shift,
            ActivityType = escala.ActivityType,
            GroupId = escala.GroupId,
            GroupCode = escala.Group?.Code,
            GroupName = escala.Group?.Name,
            LocationId = escala.LocationId,
            LocationName = escala.Location?.Name,
            StartDate = escala.StartDate,
            EndDate = escala.EndDate,
            Current = escala.StartDate <= hoje && escala.EndDate >= hoje,
            Students = membros
                .Where(m => m.GroupId == escala.GroupId)
                .Select(m => new StudentLookupDto
                {
                    StudentId = m.StudentId,
                    FullName = m.Student.FullName,
                    Rgm = m.Student.Rgm,
                    Semester = m.Student.Semester,
                    Shift = escala.Shift,
                    PeriodLabel = escala.PeriodLabel,
                    GroupId = escala.GroupId,
                    GroupCode = escala.Group?.Code,
                    GroupName = escala.Group?.Name,
                    ScheduleId = escala.Id,
                    LocationId = escala.LocationId,
                    LocationName = escala.Location?.Name,
                    ActivityType = escala.ActivityType,
                    FollowUpStart = escala.StartDate,
                    FollowUpEnd = escala.EndDate
                })
                .ToList()
        })
        // Rodízio vigente primeiro: é o que o preceptor procura no dia a dia.
        .OrderByDescending(e => e.Current)
        .ThenByDescending(e => e.StartDate)
        .ToList();

        return Ok(resultado);
    }

    /// <summary>O acompanhamento do aluno é realizado pelo preceptor.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Preceptor)]
    public async Task<ActionResult<FollowupDto>> Create([FromBody] CreateFollowupDto dto)
    {
        var userId = CurrentUserId();

        var aluno = await db.Users
            .Include(u => u.GroupMembership)
            .FirstOrDefaultAsync(u => u.Id == dto.StudentId);

        if (aluno == null || aluno.Role != "aluno")
            return BadRequest(new { message = "Aluno inválido." });

        var f = new FormativeFollowup
        {
            StudentId = dto.StudentId,
            PreceptorId = userId,
            ScheduleId = dto.ScheduleId,
            GroupId = dto.GroupId ?? aluno.GroupMembership?.GroupId,
            LocationId = dto.LocationId,
            Shift = dto.Shift ?? aluno.Shift,
            PeriodLabel = dto.PeriodLabel,
            Semester = dto.Semester ?? (aluno.Semester.HasValue ? aluno.Semester.ToString() : null),
            FollowUpStart = dto.FollowUpStart,
            FollowUpEnd = dto.FollowUpEnd
        };

        AplicarConteudo(f, dto);

        db.FormativeFollowups.Add(f);
        await db.SaveChangesAsync();

        await db.Entry(f).Reference(x => x.Student).LoadAsync();
        await db.Entry(f).Reference(x => x.Preceptor).LoadAsync();
        if (f.LocationId.HasValue) await db.Entry(f).Reference(x => x.Location).LoadAsync();

        return Ok(Map(f));
    }

    /// <summary>Somente o preceptor responsável edita o conteúdo do acompanhamento.</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Preceptor)]
    public async Task<ActionResult<FollowupDto>> Update(Guid id, [FromBody] UpdateFollowupDto dto)
    {
        var userId = CurrentUserId();

        var f = await db.FormativeFollowups
            .Include(x => x.Student).Include(x => x.Preceptor).Include(x => x.Location)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (f == null) return NotFound();
        if (f.PreceptorId != userId)
            return Forbid();
        if (f.Status == StatusCienciaAluno)
            return Conflict(new { message = "Documento finalizado e com ciência do aluno." });
        if (f.Status == StatusFinalizadoPreceptor)
            return Conflict(new { message = "Conteúdo bloqueado após finalização do preceptor." });

        AplicarConteudo(f, dto);
        f.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(Map(f));
    }

    /// <summary>Preceptor finaliza o acompanhamento e o libera para ciência do aluno.</summary>
    [HttpPost("{id}/finalize-preceptor")]
    [Authorize(Roles = Roles.Preceptor)]
    public async Task<ActionResult<FollowupDto>> FinalizePreceptor(Guid id, [FromBody] FinalizeFollowupDto dto)
    {
        var userId = CurrentUserId();

        var f = await db.FormativeFollowups
            .Include(x => x.Student).Include(x => x.Preceptor).Include(x => x.Location)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (f == null) return NotFound();
        if (f.PreceptorId != userId) return Forbid();
        if (f.Status != StatusRascunho)
            return Conflict(new { message = "Já finalizado." });

        f.Status = StatusFinalizadoPreceptor;
        f.PreceptorSignedAt = DateTime.UtcNow;
        f.PreceptorSignedName = dto.SignerName;
        f.PreceptorSignedIp = IpDaRequisicao();
        f.PreceptorSignedUserId = userId;
        f.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(Map(f));
    }

    /// <summary>Aluno dá ciência do acompanhamento realizado pelo preceptor.</summary>
    [HttpPost("{id}/finalize-student")]
    [Authorize(Roles = Roles.Aluno)]
    public async Task<ActionResult<FollowupDto>> FinalizeStudent(Guid id, [FromBody] FinalizeFollowupDto dto)
    {
        var userId = CurrentUserId();

        var f = await db.FormativeFollowups
            .Include(x => x.Student).Include(x => x.Preceptor).Include(x => x.Location)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (f == null) return NotFound();
        if (f.StudentId != userId) return Forbid();
        if (f.Status != StatusFinalizadoPreceptor)
            return Conflict(new { message = "Aguardando finalização do preceptor." });

        f.Status = StatusCienciaAluno;
        f.StudentSignedAt = DateTime.UtcNow;
        f.StudentSignedName = dto.SignerName;
        f.StudentSignedIp = IpDaRequisicao();
        f.StudentSignedUserId = userId;
        f.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(Map(f));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")!);

    private string CurrentRole() => User.FindFirstValue(ClaimTypes.Role) ?? "";

    private string? IpDaRequisicao() =>
        httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private static bool PodeVisualizar(FormativeFollowup f, string role, Guid userId) => role switch
    {
        Roles.Aluno => f.StudentId == userId && f.Status != StatusRascunho,
        Roles.Preceptor => f.PreceptorId == userId,
        Roles.Supervisor or Roles.Coordenadora => f.Status != StatusRascunho,
        _ => false
    };

    private static void AplicarConteudo(FormativeFollowup f, UpdateFollowupDto dto)
    {
        f.PosturaPontualidade = dto.PosturaPontualidade;
        f.PosturaEtica = dto.PosturaEtica;
        f.PosturaResponsabilidade = dto.PosturaResponsabilidade;
        f.ComunicacaoEquipe = dto.ComunicacaoEquipe;
        f.ComunicacaoPaciente = dto.ComunicacaoPaciente;
        f.ComunicacaoEscuta = dto.ComunicacaoEscuta;
        f.OrganizacaoPlanejamento = dto.OrganizacaoPlanejamento;
        f.OrganizacaoSeguranca = dto.OrganizacaoSeguranca;
        f.OrganizacaoRegistros = dto.OrganizacaoRegistros;
        f.ParticipacaoIniciativa = dto.ParticipacaoIniciativa;
        f.ParticipacaoAprendizado = dto.ParticipacaoAprendizado;
        f.ParticipacaoAutocritica = dto.ParticipacaoAutocritica;
        f.Potencialidades = dto.Potencialidades;
        f.AspectosAprimorar = dto.AspectosAprimorar;
        f.SituacoesRelevantes = dto.SituacoesRelevantes;
        f.ObservacoesDocente = dto.ObservacoesDocente;
        f.EvolucaoSemanal = dto.EvolucaoSemanal;
    }

    private static FollowupDto Map(FormativeFollowup f) => new()
    {
        Id = f.Id, StudentId = f.StudentId, StudentName = f.Student?.FullName ?? string.Empty,
        StudentRgm = f.Student?.Rgm,
        PreceptorId = f.PreceptorId, PreceptorName = f.Preceptor?.FullName ?? string.Empty,
        ScheduleId = f.ScheduleId, GroupId = f.GroupId, LocationId = f.LocationId,
        LocationName = f.Location?.Name, Shift = f.Shift, PeriodLabel = f.PeriodLabel,
        Semester = f.Semester, FollowUpStart = f.FollowUpStart, FollowUpEnd = f.FollowUpEnd,
        PosturaPontualidade = f.PosturaPontualidade, PosturaEtica = f.PosturaEtica,
        PosturaResponsabilidade = f.PosturaResponsabilidade,
        ComunicacaoEquipe = f.ComunicacaoEquipe, ComunicacaoPaciente = f.ComunicacaoPaciente,
        ComunicacaoEscuta = f.ComunicacaoEscuta, OrganizacaoPlanejamento = f.OrganizacaoPlanejamento,
        OrganizacaoSeguranca = f.OrganizacaoSeguranca, OrganizacaoRegistros = f.OrganizacaoRegistros,
        ParticipacaoIniciativa = f.ParticipacaoIniciativa, ParticipacaoAprendizado = f.ParticipacaoAprendizado,
        ParticipacaoAutocritica = f.ParticipacaoAutocritica, Potencialidades = f.Potencialidades,
        AspectosAprimorar = f.AspectosAprimorar, SituacoesRelevantes = f.SituacoesRelevantes,
        ObservacoesDocente = f.ObservacoesDocente, EvolucaoSemanal = f.EvolucaoSemanal,
        Status = f.Status, PreceptorSignedAt = f.PreceptorSignedAt, PreceptorSignedName = f.PreceptorSignedName,
        StudentSignedAt = f.StudentSignedAt, StudentSignedName = f.StudentSignedName,
        CreatedAt = f.CreatedAt, UpdatedAt = f.UpdatedAt
    };
}
