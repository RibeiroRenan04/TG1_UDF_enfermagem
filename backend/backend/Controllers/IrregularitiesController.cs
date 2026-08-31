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
/// Fluxo de irregularidades do ponto:
/// 1. o aluno registra (ou o sistema gera a partir de um registro de presença);
/// 2. o preceptor toma ciência;
/// 3. o preceptor pode inserir uma justificativa/observação;
/// 4. a ocorrência é encaminhada ao professor;
/// 5. o professor analisa;
/// 6. o professor aprova, nega ou acrescenta parecer.
///
/// O preceptor não valida nem altera a situação da irregularidade em nenhum ponto
/// do fluxo — essa decisão é exclusiva do professor.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IrregularitiesController(AppDbContext db) : ControllerBase
{
    // ── Listagem ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Aluno vê as próprias ocorrências; o preceptor vê as dos alunos das escalas
    /// que supervisiona; professor e coordenadora veem todas.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<IrregularityDto>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] Guid? studentId)
    {
        var userId = UsuarioAtual();
        var role = PapelAtual();

        var query = db.PointIrregularities
            .Include(i => i.Student)
            .Include(i => i.Preceptor)
            .Include(i => i.Professor)
            .AsQueryable();

        if (role == Roles.Aluno)
        {
            query = query.Where(i => i.StudentId == userId);
        }
        else if (role == Roles.Preceptor)
        {
            var alunoIds = await AlunosDoPreceptorAsync(userId);
            query = query.Where(i => alunoIds.Contains(i.StudentId));
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(i => i.Status == status);

        if (studentId.HasValue && role != Roles.Aluno)
            query = query.Where(i => i.StudentId == studentId.Value);

        var itens = await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Ok(itens.Select(Map));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<IrregularityDto>> GetById(Guid id)
    {
        var irregularidade = await CarregarAsync(id);
        if (irregularidade == null) return NotFound();

        if (!await PodeVerAsync(irregularidade)) return Forbid();

        return Ok(Map(irregularidade));
    }

    // ── 1. O aluno registra a irregularidade ──────────────────────────────────
    [HttpPost]
    [Authorize(Roles = Roles.Aluno)]
    public async Task<ActionResult<IrregularityDto>> Create([FromBody] CreateIrregularityDto dto)
    {
        var userId = UsuarioAtual();

        if (!PointIrregularity.TiposValidos.Contains(dto.Type))
            return BadRequest(new { message = "Tipo de irregularidade inválido." });

        if (dto.OccurredOn > BrasiliaTime.Hoje)
            return BadRequest(new { message = "A data da ocorrência não pode ser futura." });

        // O registro de presença informado precisa ser do próprio aluno.
        if (dto.AttendanceRecordId.HasValue)
        {
            var pertence = await db.AttendanceRecords
                .AnyAsync(r => r.Id == dto.AttendanceRecordId.Value && r.StudentId == userId);
            if (!pertence)
                return BadRequest(new { message = "Registro de presença não encontrado para este aluno." });
        }

        var irregularidade = new PointIrregularity
        {
            StudentId = userId,
            AttendanceRecordId = dto.AttendanceRecordId,
            ScheduleId = dto.ScheduleId,
            Type = dto.Type,
            OccurredOn = dto.OccurredOn,
            Description = dto.Description.Trim(),
            Status = PointIrregularity.StatusAguardandoPreceptor
        };

        db.PointIrregularities.Add(irregularidade);
        await db.SaveChangesAsync();

        await db.Entry(irregularidade).Reference(i => i.Student).LoadAsync();
        return Ok(Map(irregularidade));
    }

    // ── 2/3/4. O preceptor toma ciência, observa e encaminha ao professor ─────
    /// <summary>
    /// Registra a ciência do preceptor e a observação dele, encaminhando a
    /// ocorrência ao professor. O preceptor não aprova nem nega: a situação passa
    /// obrigatoriamente para "aguardando_professor".
    /// </summary>
    [HttpPatch("{id}/preceptor-review")]
    [Authorize(Roles = Roles.Preceptor)]
    public async Task<ActionResult<IrregularityDto>> PreceptorReview(
        Guid id, [FromBody] PreceptorReviewIrregularityDto dto)
    {
        var userId = UsuarioAtual();

        var irregularidade = await CarregarAsync(id);
        if (irregularidade == null) return NotFound();

        var alunoIds = await AlunosDoPreceptorAsync(userId);
        if (!alunoIds.Contains(irregularidade.StudentId))
            return Forbid();

        if (irregularidade.Decidida)
            return BadRequest(new { message = "Ocorrência já analisada pelo professor." });

        irregularidade.PreceptorId = userId;
        irregularidade.PreceptorNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        irregularidade.PreceptorAcknowledgedAt = BrasiliaTime.Agora;
        irregularidade.Status = PointIrregularity.StatusAguardandoProfessor;
        irregularidade.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();

        await db.Entry(irregularidade).Reference(i => i.Preceptor).LoadAsync();
        return Ok(Map(irregularidade));
    }

    // ── 5/6. O professor analisa e decide ─────────────────────────────────────
    [HttpPatch("{id}/professor-decision")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<IrregularityDto>> ProfessorDecision(
        Guid id, [FromBody] ProfessorDecisionIrregularityDto dto)
    {
        var userId = UsuarioAtual();

        var irregularidade = await CarregarAsync(id);
        if (irregularidade == null) return NotFound();

        irregularidade.ProfessorId = userId;
        irregularidade.ProfessorNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        irregularidade.ProfessorDecidedAt = BrasiliaTime.Agora;
        irregularidade.Status = dto.Approve
            ? PointIrregularity.StatusAprovada
            : PointIrregularity.StatusNegada;
        irregularidade.UpdatedAt = BrasiliaTime.Agora;

        // A decisão do professor reflete na situação do registro de ponto de origem:
        // aprovar a justificativa regulariza a presença; negar mantém a irregularidade.
        if (irregularidade.AttendanceRecordId.HasValue)
        {
            var registro = await db.AttendanceRecords
                .FirstOrDefaultAsync(r => r.Id == irregularidade.AttendanceRecordId.Value);
            if (registro != null)
            {
                registro.Status = dto.Approve ? "aprovado" : "irregular";
                registro.ValidatedById = userId;
                registro.ValidatedAt = BrasiliaTime.Agora;
                if (!dto.Approve && !string.IsNullOrWhiteSpace(dto.Note))
                    registro.IrregularityReason = dto.Note.Trim();
            }
        }

        await db.SaveChangesAsync();

        await db.Entry(irregularidade).Reference(i => i.Professor).LoadAsync();
        return Ok(Map(irregularidade));
    }

    // ── Contadores para os painéis ────────────────────────────────────────────
    [HttpGet("summary")]
    public async Task<ActionResult> GetSummary()
    {
        var userId = UsuarioAtual();
        var role = PapelAtual();

        var query = db.PointIrregularities.AsQueryable();

        if (role == Roles.Aluno)
        {
            query = query.Where(i => i.StudentId == userId);
        }
        else if (role == Roles.Preceptor)
        {
            var alunoIds = await AlunosDoPreceptorAsync(userId);
            query = query.Where(i => alunoIds.Contains(i.StudentId));
        }

        var porStatus = await query
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Total = g.Count() })
            .ToListAsync();

        int Contar(string s) => porStatus.FirstOrDefault(x => x.Status == s)?.Total ?? 0;

        return Ok(new
        {
            aguardandoPreceptor = Contar(PointIrregularity.StatusAguardandoPreceptor),
            aguardandoProfessor = Contar(PointIrregularity.StatusAguardandoProfessor),
            aprovadas = Contar(PointIrregularity.StatusAprovada),
            negadas = Contar(PointIrregularity.StatusNegada),
            total = porStatus.Sum(x => x.Total)
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Guid UsuarioAtual() => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    private string PapelAtual() => User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;

    private Task<PointIrregularity?> CarregarAsync(Guid id) =>
        db.PointIrregularities
            .Include(i => i.Student)
            .Include(i => i.Preceptor)
            .Include(i => i.Professor)
            .FirstOrDefaultAsync(i => i.Id == id);

    /// <summary>Alunos dos grupos das escalas em que o preceptor atua.</summary>
    private async Task<List<Guid>> AlunosDoPreceptorAsync(Guid preceptorId)
    {
        var grupoIds = await db.RotationSchedules
            .Where(s => s.PreceptorId == preceptorId)
            .Select(s => s.GroupId)
            .Distinct()
            .ToListAsync();

        return await db.GroupMemberships
            .Where(m => grupoIds.Contains(m.GroupId))
            .Select(m => m.StudentId)
            .ToListAsync();
    }

    private async Task<bool> PodeVerAsync(PointIrregularity irregularidade)
    {
        var userId = UsuarioAtual();
        var role = PapelAtual();

        return role switch
        {
            Roles.Aluno => irregularidade.StudentId == userId,
            Roles.Preceptor => (await AlunosDoPreceptorAsync(userId)).Contains(irregularidade.StudentId),
            _ => true
        };
    }

    private static IrregularityDto Map(PointIrregularity i) => new()
    {
        Id = i.Id,
        StudentId = i.StudentId,
        StudentName = i.Student?.FullName ?? string.Empty,
        StudentRgm = i.Student?.Rgm,
        AttendanceRecordId = i.AttendanceRecordId,
        ScheduleId = i.ScheduleId,
        Type = i.Type,
        OccurredOn = i.OccurredOn,
        Description = i.Description,
        Status = i.Status,
        PreceptorId = i.PreceptorId,
        PreceptorName = i.Preceptor?.FullName,
        PreceptorNote = i.PreceptorNote,
        PreceptorAcknowledgedAt = i.PreceptorAcknowledgedAt,
        ProfessorId = i.ProfessorId,
        ProfessorName = i.Professor?.FullName,
        ProfessorNote = i.ProfessorNote,
        ProfessorDecidedAt = i.ProfessorDecidedAt,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt
    };
}
