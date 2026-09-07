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
/// Calendário de exceções: feriados, recessos, estágios cancelados, trocas de
/// local, dias que viraram remotos, atividades especiais e reposições.
///
/// A exceção é cadastrada uma vez com a abrangência certa (faculdade, curso,
/// turma, rodízio ou um aluno) e a programação de cada data a aplica sozinha —
/// não é preciso mexer aluno por aluno.
/// </summary>
[ApiController]
[Route("api/excecoes-calendario")]
[Authorize]
public class ExcecoesCalendarioController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ExcecaoCalendarioDto>>> GetAll(
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        [FromQuery] string? tipo,
        [FromQuery] string? abrangencia,
        [FromQuery] Guid? groupId)
    {
        var query = Base();

        if (de.HasValue) query = query.Where(x => x.EndDate >= de.Value);
        if (ate.HasValue) query = query.Where(x => x.StartDate <= ate.Value);
        if (!string.IsNullOrWhiteSpace(tipo)) query = query.Where(x => x.Type == tipo);
        if (!string.IsNullOrWhiteSpace(abrangencia)) query = query.Where(x => x.Scope == abrangencia);
        if (groupId.HasValue) query = query.Where(x => x.GroupId == groupId.Value);

        var excecoes = await query
            .OrderByDescending(x => x.StartDate)
            .ToListAsync();

        return Ok(excecoes.Select(Map));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ExcecaoCalendarioDto>> Get(Guid id)
    {
        var excecao = await Base().FirstOrDefaultAsync(x => x.Id == id);
        return excecao == null ? NotFound() : Ok(Map(excecao));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<ExcecaoCalendarioDto>> Create([FromBody] CriarExcecaoCalendarioDto dto)
    {
        var erro = await ValidarAsync(dto);
        if (erro != null) return BadRequest(new { message = erro });

        var excecao = Aplicar(new CalendarException
        {
            CreatedById = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")!)
        }, dto);

        db.CalendarExceptions.Add(excecao);
        await db.SaveChangesAsync();

        return Ok(Map((await Base().FirstAsync(x => x.Id == excecao.Id))));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<ExcecaoCalendarioDto>> Update(Guid id, [FromBody] CriarExcecaoCalendarioDto dto)
    {
        var excecao = await db.CalendarExceptions.FirstOrDefaultAsync(x => x.Id == id);
        if (excecao == null) return NotFound();

        var erro = await ValidarAsync(dto);
        if (erro != null) return BadRequest(new { message = erro });

        Aplicar(excecao, dto);
        excecao.UpdatedAt = BrasiliaTime.Agora;
        await db.SaveChangesAsync();

        return Ok(Map(await Base().FirstAsync(x => x.Id == id)));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var excecao = await db.CalendarExceptions.FirstOrDefaultAsync(x => x.Id == id);
        if (excecao == null) return NotFound();

        db.CalendarExceptions.Remove(excecao);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── Validação ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Confere o tipo, a abrangência e o alvo correspondente. Cada abrangência exige
    /// o seu alvo: sem isso a exceção alcançaria a faculdade inteira sem querer.
    /// </summary>
    private async Task<string?> ValidarAsync(CriarExcecaoCalendarioDto dto)
    {
        if (!TipoExcecao.Valido(dto.Type)) return "Tipo de exceção inválido.";
        if (!AbrangenciaExcecao.Valida(dto.Scope)) return "Abrangência inválida.";

        var fim = dto.EndDate ?? dto.StartDate;
        if (fim < dto.StartDate)
            return "A data final não pode ser anterior à inicial.";

        if (dto.Shift != null && Turnos.Normalizar(dto.Shift) == null)
            return "Turno inválido. Use manhã, tarde ou noite.";

        switch (dto.Scope)
        {
            case AbrangenciaExcecao.Turma:
                if (!dto.GroupId.HasValue) return "Selecione a turma alcançada pela exceção.";
                if (!await db.StudentGroups.AnyAsync(g => g.Id == dto.GroupId.Value))
                    return "Turma não encontrada.";
                break;

            case AbrangenciaExcecao.Rodizio:
                if (!dto.ScheduleId.HasValue) return "Selecione o rodízio alcançado pela exceção.";
                if (!await db.RotationSchedules.AnyAsync(s => s.Id == dto.ScheduleId.Value))
                    return "Rodízio não encontrado.";
                break;

            case AbrangenciaExcecao.Aluno:
                if (!dto.StudentId.HasValue) return "Selecione o aluno alcançado pela exceção.";
                if (!await db.Users.AnyAsync(u => u.Id == dto.StudentId.Value && u.Role == Roles.Aluno))
                    return "Aluno não encontrado.";
                break;

            case AbrangenciaExcecao.Curso:
                if (string.IsNullOrWhiteSpace(dto.Course)) return "Informe o curso alcançado pela exceção.";
                break;
        }

        // A troca de local só faz sentido com o local de destino.
        if (dto.Type == TipoExcecao.TrocaLocal)
        {
            if (!dto.LocationId.HasValue)
                return "Selecione a unidade que passa a valer na troca de local.";
            if (!await db.Locations.AnyAsync(l => l.Id == dto.LocationId.Value))
                return "Unidade não encontrada.";
        }

        if (dto.RemoteActivityId.HasValue
            && !await db.RemoteActivities.AnyAsync(a => a.Id == dto.RemoteActivityId.Value))
            return "Atividade remota não encontrada.";

        return null;
    }

    private static CalendarException Aplicar(CalendarException excecao, CriarExcecaoCalendarioDto dto)
    {
        excecao.Type = dto.Type;
        excecao.Scope = dto.Scope;
        excecao.StartDate = dto.StartDate;
        excecao.EndDate = dto.EndDate ?? dto.StartDate;
        excecao.Shift = Turnos.Normalizar(dto.Shift);
        // O alvo acompanha a abrangência: guardar os outros deixaria vínculos mortos
        // que confundem a listagem e a resolução da programação.
        excecao.GroupId = dto.Scope == AbrangenciaExcecao.Turma ? dto.GroupId : null;
        excecao.ScheduleId = dto.Scope == AbrangenciaExcecao.Rodizio ? dto.ScheduleId : null;
        excecao.StudentId = dto.Scope == AbrangenciaExcecao.Aluno ? dto.StudentId : null;
        excecao.Course = dto.Scope == AbrangenciaExcecao.Curso ? dto.Course?.Trim() : null;
        excecao.LocationId = dto.LocationId;
        excecao.RemoteActivityId = dto.Type == TipoExcecao.Remoto ? dto.RemoteActivityId : null;
        excecao.Description = dto.Description.Trim();
        return excecao;
    }

    private IQueryable<CalendarException> Base() =>
        db.CalendarExceptions
            .Include(x => x.Group)
            .Include(x => x.Schedule)
            .Include(x => x.Student)
            .Include(x => x.Location)
            .Include(x => x.RemoteActivity)
            .Include(x => x.CreatedBy);

    private static ExcecaoCalendarioDto Map(CalendarException x) => new()
    {
        Id = x.Id,
        Type = x.Type,
        TypeLabel = TipoExcecao.Rotulo(x.Type),
        Scope = x.Scope,
        ScopeLabel = AbrangenciaExcecao.Rotulo(x.Scope),
        StartDate = x.StartDate,
        EndDate = x.EndDate,
        Shift = x.Shift,
        GroupId = x.GroupId,
        GroupCode = x.Group?.Code,
        ScheduleId = x.ScheduleId,
        PeriodLabel = x.Schedule?.PeriodLabel,
        StudentId = x.StudentId,
        StudentName = x.Student?.FullName,
        Course = x.Course,
        LocationId = x.LocationId,
        LocationName = x.Location?.Name,
        RemoteActivityId = x.RemoteActivityId,
        RemoteActivityTitle = x.RemoteActivity?.Title,
        Description = x.Description,
        DispensaPonto = TipoExcecao.DispensaPonto(x.Type),
        CreatedByName = x.CreatedBy?.FullName,
        CreatedAt = x.CreatedAt
    };
}
