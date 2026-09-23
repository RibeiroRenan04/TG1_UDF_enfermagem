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
/// Fluxo: aluno registra (ou o sistema gera) → preceptor toma ciência e observa → professor
/// decide. O preceptor nunca altera a situação da irregularidade.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IrregularitiesController(
    AppDbContext db, IrregularidadesPainelService painel, EscopoPreceptorService escopo) : ControllerBase
{
    /// <summary>Aluno vê as próprias; preceptor, as dos alunos das escalas dele; professor e coordenadora, todas.</summary>
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
            .Include(i => i.AttendanceRecord).ThenInclude(r => r!.Location)
            .AsNoTracking()
            .AsQueryable();

        if (role == Roles.Aluno)
        {
            query = query.Where(i => i.StudentId == userId);
        }
        else if (role == Roles.Preceptor)
        {
            query = EscopoPreceptorService.FiltrarOcorrencias(query, await escopo.CarregarAsync(userId));
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

    [HttpPost]
    [Authorize(Roles = Roles.Aluno)]
    public async Task<ActionResult<IrregularityDto>> Create([FromBody] CreateIrregularityDto dto)
    {
        var userId = UsuarioAtual();

        if (!PointIrregularity.TiposValidos.Contains(dto.Type))
            return BadRequest(new { message = "Tipo de irregularidade inválido." });

        if (dto.OccurredOn > BrasiliaTime.Hoje)
            return BadRequest(new { message = "A data da ocorrência não pode ser futura." });

        if (dto.AttendanceRecordId.HasValue)
        {
            var pertence = await db.AttendanceRecords
                .AnyAsync(r => r.Id == dto.AttendanceRecordId.Value && r.StudentId == userId);
            if (!pertence)
                return BadRequest(new { message = "Registro de presença não encontrado para este aluno." });
        }

        // Um ponto tem uma contestação por vez, até ela ser negada.
        var emAberto = await OcorrenciaEmAbertoAsync(userId, dto.AttendanceRecordId, dto.Type, dto.OccurredOn);
        if (emAberto != null)
            return Conflict(new
            {
                message = dto.AttendanceRecordId.HasValue
                    ? "Já existe uma irregularidade em análise para este ponto. "
                      + "Aguarde a decisão do professor — se ela for recusada, você poderá abrir outra."
                    : "Você já registrou uma irregularidade deste tipo nesta data e ela ainda está em análise.",
                code = "irregularidade_em_aberto",
                irregularityId = emAberto.Id,
                status = emAberto.Status
            });

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

        return Ok(Map((await CarregarAsync(irregularidade.Id))!));
    }

    /// <summary>Com ponto vinculado a trava é por ponto; sem ponto, por tipo + data.</summary>
    private async Task<PointIrregularity?> OcorrenciaEmAbertoAsync(
        Guid studentId, Guid? attendanceRecordId, string tipo, DateOnly ocorridaEm)
    {
        var query = db.PointIrregularities
            .Where(i => i.StudentId == studentId && i.Status != PointIrregularity.StatusNegada);

        query = attendanceRecordId.HasValue
            ? query.Where(i => i.AttendanceRecordId == attendanceRecordId.Value)
            : query.Where(i => i.AttendanceRecordId == null
                            && i.Type == tipo
                            && i.OccurredOn == ocorridaEm);

        return await query.OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync();
    }

    /// <summary>O preceptor não aprova nem nega: a situação passa para "aguardando_professor".</summary>
    [HttpPatch("{id}/preceptor-review")]
    [Authorize(Roles = Roles.Preceptor)]
    public async Task<ActionResult<IrregularityDto>> PreceptorReview(
        Guid id, [FromBody] PreceptorReviewIrregularityDto dto)
    {
        var userId = UsuarioAtual();

        var irregularidade = await CarregarAsync(id);
        if (irregularidade == null) return NotFound();

        if (!await escopo.AlcancaOcorrenciaAsync(userId, id))
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

        // Aprovar regulariza o ponto de origem; negar mantém a irregularidade.
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

    /// <summary><paramref name="dias"/> é a janela analisada (7 a 365; vazio = todo o histórico). A fila é sempre a atual.</summary>
    [HttpGet("painel")]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<IrregularidadesPainelDto>> GetPainel([FromQuery] int? dias, CancellationToken ct)
    {
        if (dias is < 7 or > 365)
            return BadRequest(ErrosApi.Corpo("O período deve ter entre 7 e 365 dias.", "dias", "periodo_invalido"));

        return Ok(await painel.MontarAsync(dias, ct));
    }

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
            query = EscopoPreceptorService.FiltrarOcorrencias(query, await escopo.CarregarAsync(userId));
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

    private Guid UsuarioAtual() => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    private string PapelAtual() => User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;

    private Task<PointIrregularity?> CarregarAsync(Guid id) =>
        db.PointIrregularities
            .Include(i => i.Student)
            .Include(i => i.Preceptor)
            .Include(i => i.Professor)
            .Include(i => i.AttendanceRecord).ThenInclude(r => r!.Location)
            .FirstOrDefaultAsync(i => i.Id == id);

    private async Task<bool> PodeVerAsync(PointIrregularity irregularidade)
    {
        var userId = UsuarioAtual();
        var role = PapelAtual();

        return role switch
        {
            Roles.Aluno => irregularidade.StudentId == userId,
            Roles.Preceptor => await escopo.AlcancaOcorrenciaAsync(userId, irregularidade.Id),
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
        AttendanceRecordedAt = i.AttendanceRecord?.RecordedAt,
        AttendanceType = i.AttendanceRecord?.Type,
        AttendanceLocationName = i.AttendanceRecord?.Location?.Name,
        AttendanceStatus = i.AttendanceRecord?.Status,
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
