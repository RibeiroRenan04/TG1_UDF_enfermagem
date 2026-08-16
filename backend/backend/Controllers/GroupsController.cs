using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GroupsController(AppDbContext db) : ControllerBase
{
    private static readonly string[] TurnosValidos = ["manha", "tarde", "noite"];
    private static readonly string[] AtividadesValidas = ["gestao", "pic", "assistencia", "outro"];

    [HttpGet]
    public async Task<ActionResult<List<GroupDto>>> GetAll()
    {
        var groups = await db.StudentGroups
            .Include(g => g.Memberships)
            .OrderBy(g => g.Code)
            .ToListAsync();

        return Ok(groups.Select(g => new GroupDto
        {
            Id = g.Id, Code = g.Code, Name = g.Name, Description = g.Description,
            MemberCount = g.Memberships.Count
        }));
    }

    [HttpPost]
    [Authorize(Roles = "supervisor")]
    public async Task<ActionResult<GroupDto>> Create([FromBody] CreateGroupDto dto)
    {
        var code = dto.Code.Trim().ToUpper();
        if (await db.StudentGroups.AnyAsync(g => g.Code == code))
            return Conflict(new { message = "Código já utilizado." });

        var group = new StudentGroup
        {
            Code = code,
            Name = dto.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim()
        };

        db.StudentGroups.Add(group);
        await db.SaveChangesAsync();
        return Ok(new GroupDto { Id = group.Id, Code = group.Code, Name = group.Name, Description = group.Description, MemberCount = 0 });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "supervisor")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var group = await db.StudentGroups.FindAsync(id);
        if (group == null) return NotFound();
        db.StudentGroups.Remove(group);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Alunos vinculados à turma. Permite conferir o vínculo antes de alocar o rodízio.
    /// </summary>
    [HttpGet("{id}/members")]
    public async Task<ActionResult<List<GroupMemberDto>>> GetMembers(Guid id)
    {
        if (!await db.StudentGroups.AnyAsync(g => g.Id == id))
            return NotFound(new { message = "Turma não encontrada." });

        var members = await db.GroupMemberships
            .Include(m => m.Student)
            .Where(m => m.GroupId == id)
            .OrderBy(m => m.Student.FullName)
            .Select(m => new GroupMemberDto
            {
                StudentId = m.StudentId,
                FullName = m.Student.FullName,
                Rgm = m.Student.Rgm,
                Semester = m.Student.Semester,
                Shift = m.Student.Shift,
                IsActive = m.Student.IsActive
            })
            .ToListAsync();

        return Ok(members);
    }

    // ── Schedules ──────────────────────────────────────────────────────────────
    [HttpGet("schedules")]
    public async Task<ActionResult<List<ScheduleDto>>> GetSchedules()
    {
        var schedules = await db.RotationSchedules
            .Include(s => s.Group)
            .Include(s => s.Location)
            .Include(s => s.Preceptor)
            .OrderBy(s => s.StartDate)
            .ToListAsync();

        return Ok(schedules.Select(MapSchedule));
    }

    /// <summary>Escalas de rodízio de uma turma específica.</summary>
    [HttpGet("{groupId}/schedules")]
    public async Task<ActionResult<List<ScheduleDto>>> GetSchedulesByGroup(Guid groupId)
    {
        var schedules = await db.RotationSchedules
            .Include(s => s.Group)
            .Include(s => s.Location)
            .Include(s => s.Preceptor)
            .Where(s => s.GroupId == groupId)
            .OrderBy(s => s.StartDate)
            .ToListAsync();

        return Ok(schedules.Select(MapSchedule));
    }

    [HttpPost("schedules")]
    [Authorize(Roles = "supervisor")]
    public async Task<ActionResult<ScheduleDto>> CreateSchedule([FromBody] CreateScheduleDto dto)
    {
        var erro = await ValidarAlocacaoAsync(dto);
        if (erro != null) return BadRequest(new { message = erro });

        var schedule = new RotationSchedule
        {
            GroupId = dto.GroupId,
            LocationId = dto.LocationId,
            PreceptorId = dto.PreceptorId,
            Shift = dto.Shift.Trim().ToLower(),
            PeriodLabel = dto.PeriodLabel.Trim(),
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            ActivityType = dto.ActivityType.Trim().ToLower(),
            RequiredHours = dto.RequiredHours,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
        };

        db.RotationSchedules.Add(schedule);
        await db.SaveChangesAsync();

        return Ok(MapSchedule(await RecarregarAsync(schedule.Id)));
    }

    [HttpPut("schedules/{id}")]
    [Authorize(Roles = "supervisor")]
    public async Task<ActionResult<ScheduleDto>> UpdateSchedule(Guid id, [FromBody] CreateScheduleDto dto)
    {
        var schedule = await db.RotationSchedules.FirstOrDefaultAsync(s => s.Id == id);
        if (schedule == null) return NotFound();

        var erro = await ValidarAlocacaoAsync(dto, id);
        if (erro != null) return BadRequest(new { message = erro });

        schedule.GroupId = dto.GroupId;
        schedule.LocationId = dto.LocationId;
        schedule.PreceptorId = dto.PreceptorId;
        schedule.Shift = dto.Shift.Trim().ToLower();
        schedule.PeriodLabel = dto.PeriodLabel.Trim();
        schedule.StartDate = dto.StartDate;
        schedule.EndDate = dto.EndDate;
        schedule.ActivityType = dto.ActivityType.Trim().ToLower();
        schedule.RequiredHours = dto.RequiredHours;
        schedule.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

        await db.SaveChangesAsync();

        return Ok(MapSchedule(await RecarregarAsync(id)));
    }

    [HttpDelete("schedules/{id}")]
    [Authorize(Roles = "supervisor")]
    public async Task<IActionResult> DeleteSchedule(Guid id)
    {
        var schedule = await db.RotationSchedules.FindAsync(id);
        if (schedule == null) return NotFound();
        db.RotationSchedules.Remove(schedule);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── Validação da alocação ─────────────────────────────────────────────────
    /// <summary>
    /// Confere os dados da alocação do rodízio: turma existente e com alunos
    /// vinculados, local, preceptor responsável, datas e carga horária.
    /// Devolve a mensagem de erro ou <c>null</c> quando tudo está válido.
    /// </summary>
    private async Task<string?> ValidarAlocacaoAsync(CreateScheduleDto dto, Guid? scheduleIdAtual = null)
    {
        var group = await db.StudentGroups
            .Include(g => g.Memberships)
            .FirstOrDefaultAsync(g => g.Id == dto.GroupId);

        if (group == null)
            return "Turma não encontrada.";

        // Os alunos precisam estar vinculados à turma antes da alocação.
        if (group.Memberships.Count == 0)
            return $"A turma {group.Code} não possui alunos vinculados. "
                 + "Vincule os alunos à turma antes de alocar o rodízio.";

        if (!await db.Locations.AnyAsync(l => l.Id == dto.LocationId))
            return "Local de estágio não encontrado.";

        // O responsável precisa ter perfil de preceptor: é ele quem realiza o
        // acompanhamento formativo dos alunos alocados neste rodízio.
        var preceptor = await db.Users.FirstOrDefaultAsync(u => u.Id == dto.PreceptorId);
        if (preceptor == null)
            return "Preceptor não encontrado.";
        if (preceptor.Role != "preceptor")
            return "O responsável informado não possui perfil de preceptor.";
        if (!preceptor.IsActive)
            return "O preceptor informado está inativo.";

        var turno = dto.Shift.Trim().ToLower();
        if (!TurnosValidos.Contains(turno))
            return "Turno inválido. Use manhã, tarde ou noite.";

        var atividade = dto.ActivityType.Trim().ToLower();
        if (!AtividadesValidas.Contains(atividade))
            return "Atividade inválida.";

        if (dto.EndDate < dto.StartDate)
            return "A data de término não pode ser anterior à data de início.";

        if (dto.RequiredHours <= 0)
            return "Informe uma carga horária maior que zero.";

        // Evita duas alocações simultâneas da mesma turma no mesmo turno.
        var conflito = await db.RotationSchedules
            .Include(s => s.Location)
            .Where(s => s.GroupId == dto.GroupId
                     && s.Shift == turno
                     && s.Id != scheduleIdAtual
                     && s.StartDate <= dto.EndDate
                     && s.EndDate >= dto.StartDate)
            .FirstOrDefaultAsync();

        if (conflito != null)
            return $"A turma já possui rodízio no turno informado entre "
                 + $"{conflito.StartDate:dd/MM/yyyy} e {conflito.EndDate:dd/MM/yyyy} "
                 + $"({conflito.Location?.Name}).";

        return null;
    }

    private async Task<RotationSchedule> RecarregarAsync(Guid id) =>
        await db.RotationSchedules
            .Include(s => s.Group)
            .Include(s => s.Location)
            .Include(s => s.Preceptor)
            .FirstAsync(s => s.Id == id);

    private static ScheduleDto MapSchedule(RotationSchedule s) => new()
    {
        Id = s.Id, GroupId = s.GroupId, GroupCode = s.Group?.Code ?? string.Empty,
        GroupName = s.Group?.Name,
        LocationId = s.LocationId, LocationName = s.Location?.Name ?? string.Empty,
        PreceptorId = s.PreceptorId, PreceptorName = s.Preceptor?.FullName,
        Shift = s.Shift, PeriodLabel = s.PeriodLabel,
        StartDate = s.StartDate, EndDate = s.EndDate,
        ActivityType = s.ActivityType, RequiredHours = s.RequiredHours, Notes = s.Notes
    };
}
