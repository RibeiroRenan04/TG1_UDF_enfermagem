using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
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

    /// <summary>
    /// Falha de validação amarrada ao campo do formulário, para a tela destacar
    /// exatamente o que precisa ser corrigido.
    /// </summary>
    private sealed record ErroValidacao(
        string Mensagem,
        string? Campo = null,
        int Status = StatusCodes.Status400BadRequest,
        string? Codigo = null);

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
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<GroupDto>> Create([FromBody] CreateGroupDto dto)
    {
        var code = dto.Code.Trim().ToUpper();
        if (await db.StudentGroups.AnyAsync(g => g.Code == code))
            return Conflict(ErrosApi.Corpo($"O código {code} já é usado por outra turma.", "code", "codigo_duplicado"));

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
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var group = await db.StudentGroups.FindAsync(id);
        if (group == null) return NotFound(new { message = "Turma não encontrada." });
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
            .Include(s => s.Days).ThenInclude(d => d.Location)
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
            .Include(s => s.Days).ThenInclude(d => d.Location)
            .Where(s => s.GroupId == groupId)
            .OrderBy(s => s.StartDate)
            .ToListAsync();

        return Ok(schedules.Select(MapSchedule));
    }

    [HttpPost("schedules")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<ScheduleDto>> CreateSchedule([FromBody] CreateScheduleDto dto)
    {
        var erro = await ValidarAlocacaoAsync(dto);
        if (erro != null) return Falha(erro);

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
        AplicarDias(schedule, dto.Days);
        await db.SaveChangesAsync();

        return Ok(MapSchedule(await RecarregarAsync(schedule.Id)));
    }

    [HttpPut("schedules/{id}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<ScheduleDto>> UpdateSchedule(Guid id, [FromBody] CreateScheduleDto dto)
    {
        var schedule = await db.RotationSchedules
            .Include(s => s.Days)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (schedule == null) return NotFound(new { message = "Rodízio não encontrado. Ele pode ter sido excluído." });

        var erro = await ValidarAlocacaoAsync(dto, id);
        if (erro != null) return Falha(erro);

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

        // A programação semanal é substituída inteira: enviar a lista vazia volta o
        // rodízio ao padrão (todo dia útil presencial no local principal).
        db.RotationDaySchedules.RemoveRange(schedule.Days);
        schedule.Days.Clear();
        AplicarDias(schedule, dto.Days);

        await db.SaveChangesAsync();

        return Ok(MapSchedule(await RecarregarAsync(id)));
    }

    [HttpDelete("schedules/{id}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> DeleteSchedule(Guid id)
    {
        var schedule = await db.RotationSchedules.FindAsync(id);
        if (schedule == null) return NotFound(new { message = "Rodízio não encontrado. Ele pode ter sido excluído." });
        db.RotationSchedules.Remove(schedule);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private ObjectResult Falha(ErroValidacao erro) =>
        StatusCode(erro.Status, ErrosApi.Corpo(erro.Mensagem, erro.Campo, erro.Codigo));

    // ── Validação da alocação ─────────────────────────────────────────────────
    /// <summary>
    /// Confere os dados da alocação do rodízio: turma existente e com alunos
    /// vinculados, local, preceptor responsável, datas e carga horária.
    /// Devolve o erro (com o campo afetado) ou <c>null</c> quando tudo está válido.
    /// </summary>
    private async Task<ErroValidacao?> ValidarAlocacaoAsync(CreateScheduleDto dto, Guid? scheduleIdAtual = null)
    {
        var group = await db.StudentGroups
            .Include(g => g.Memberships)
            .FirstOrDefaultAsync(g => g.Id == dto.GroupId);

        if (group == null)
            return new("Turma não encontrada.", "groupId");

        // Os alunos precisam estar vinculados à turma antes da alocação.
        if (group.Memberships.Count == 0)
            return new($"A turma {group.Code} não possui alunos vinculados. "
                     + "Vincule os alunos à turma antes de alocar o rodízio.", "groupId");

        if (!await db.Locations.AnyAsync(l => l.Id == dto.LocationId))
            return new("Local de estágio não encontrado.", "locationId");

        // O responsável precisa ter perfil de preceptor: é ele quem realiza o
        // acompanhamento formativo dos alunos alocados neste rodízio.
        var preceptor = await db.Users.FirstOrDefaultAsync(u => u.Id == dto.PreceptorId);
        if (preceptor == null)
            return new("Preceptor não encontrado.", "preceptorId");
        if (preceptor.Role != "preceptor")
            return new("O responsável informado não possui perfil de preceptor.", "preceptorId");
        if (!preceptor.IsActive)
            return new("O preceptor informado está inativo.", "preceptorId");

        var turno = dto.Shift.Trim().ToLower();
        if (!TurnosValidos.Contains(turno))
            return new("Turno inválido. Use manhã, tarde ou noite.", "shift");

        var atividade = dto.ActivityType.Trim().ToLower();
        if (!AtividadesValidas.Contains(atividade))
            return new("Atividade inválida.", "activityType");

        var erroDatas = ValidarDatas(dto.StartDate, dto.EndDate);
        if (erroDatas != null) return erroDatas;

        if (dto.RequiredHours <= 0)
            return new("Informe uma carga horária maior que zero.", "requiredHours");

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
            return new($"Conflito de agenda: a turma já possui rodízio no turno informado entre "
                     + $"{conflito.StartDate:dd/MM/yyyy} e {conflito.EndDate:dd/MM/yyyy} "
                     + $"({conflito.Location?.Name}).",
                "startDate", StatusCodes.Status409Conflict, "conflito_agenda");

        return await ValidarDiasAsync(dto.Days);
    }

    /// <summary>
    /// Datas do rodízio. Campo de data vazio chega como 01/01/0001 (o
    /// <c>[Required]</c> não pega <c>DateOnly</c>), e um ano digitado errado
    /// passava direto — foi assim que o painel do aluno chegou a contar milhares
    /// de dias sem registro.
    /// </summary>
    private static ErroValidacao? ValidarDatas(DateOnly inicio, DateOnly fim)
    {
        if (inicio == default)
            return new("Informe a data de início.", "startDate");
        if (fim == default)
            return new("Informe a data de término.", "endDate");

        if (inicio < RotationSchedule.DataMinima || inicio > RotationSchedule.DataMaxima)
            return new($"Data de início inválida ({inicio:dd/MM/yyyy}). Confira o ano informado.", "startDate");
        if (fim < RotationSchedule.DataMinima || fim > RotationSchedule.DataMaxima)
            return new($"Data de término inválida ({fim:dd/MM/yyyy}). Confira o ano informado.", "endDate");

        if (fim < inicio)
            return new("A data de término não pode ser anterior à data de início.", "endDate");

        if (fim.DayNumber - inicio.DayNumber > RotationSchedule.DuracaoMaximaDias)
            return new($"O rodízio não pode durar mais de {RotationSchedule.DuracaoMaximaDias} dias. "
                     + "Confira as datas de início e término.", "endDate");

        return null;
    }

    /// <summary>
    /// Confere a programação semanal: um dia da semana aparece uma vez só, o modo
    /// é conhecido e o local informado existe. Sem dias, o rodízio segue no padrão.
    /// </summary>
    private async Task<ErroValidacao?> ValidarDiasAsync(List<CriarDiaRodizioDto>? dias)
    {
        if (dias == null || dias.Count == 0) return null;

        var vistos = new HashSet<int>();
        foreach (var dia in dias)
        {
            if (!DiasSemana.Valido(dia.DayOfWeek))
                return new("Dia da semana inválido na programação.", "days");
            if (!vistos.Add(dia.DayOfWeek))
                return new($"{DiasSemana.Rotulo(dia.DayOfWeek)} aparece mais de uma vez na programação.", "days");

            var modo = ModoAtividade.Normalizar(dia.Mode);
            if (modo == null)
                return new($"Tipo de atividade inválido em {DiasSemana.Rotulo(dia.DayOfWeek)}.", "days");

            if (modo == ModoAtividade.Presencial && dia.LocationId.HasValue
                && !await db.Locations.AnyAsync(l => l.Id == dia.LocationId.Value))
                return new($"Local informado em {DiasSemana.Rotulo(dia.DayOfWeek)} não foi encontrado.", "days");
        }

        return null;
    }

    /// <summary>
    /// Grava a programação semanal. O local só é guardado no dia presencial: em um
    /// dia remoto ou sem atividade ele não significa nada e só confundiria a tela.
    /// </summary>
    private static void AplicarDias(RotationSchedule schedule, List<CriarDiaRodizioDto>? dias)
    {
        if (dias == null) return;

        foreach (var dia in dias)
        {
            var modo = ModoAtividade.Normalizar(dia.Mode) ?? ModoAtividade.Presencial;
            schedule.Days.Add(new RotationDaySchedule
            {
                ScheduleId = schedule.Id,
                DayOfWeek = dia.DayOfWeek,
                Mode = modo,
                LocationId = modo == ModoAtividade.Presencial ? dia.LocationId : null,
                Notes = string.IsNullOrWhiteSpace(dia.Notes) ? null : dia.Notes.Trim()
            });
        }
    }

    private async Task<RotationSchedule> RecarregarAsync(Guid id) =>
        await db.RotationSchedules
            .Include(s => s.Group)
            .Include(s => s.Location)
            .Include(s => s.Preceptor)
            .Include(s => s.Days).ThenInclude(d => d.Location)
            .FirstAsync(s => s.Id == id);

    private static ScheduleDto MapSchedule(RotationSchedule s) => new()
    {
        Id = s.Id, GroupId = s.GroupId, GroupCode = s.Group?.Code ?? string.Empty,
        GroupName = s.Group?.Name,
        LocationId = s.LocationId, LocationName = s.Location?.Name ?? string.Empty,
        PreceptorId = s.PreceptorId, PreceptorName = s.Preceptor?.FullName,
        Shift = s.Shift, PeriodLabel = s.PeriodLabel,
        StartDate = s.StartDate, EndDate = s.EndDate,
        ActivityType = s.ActivityType, RequiredHours = s.RequiredHours, Notes = s.Notes,
        Days = [.. s.Days.OrderBy(d => d.DayOfWeek == 0 ? 7 : d.DayOfWeek).Select(d => MapDia(d, s))]
    };

    /// <summary>
    /// O dia herda o local principal do rodízio quando nenhum outro foi informado —
    /// é o que a tela mostra e o que a programação usa para validar o ponto.
    /// </summary>
    private static DiaRodizioDto MapDia(RotationDaySchedule d, RotationSchedule s) => new()
    {
        DayOfWeek = d.DayOfWeek,
        DayLabel = DiasSemana.Rotulo(d.DayOfWeek),
        Mode = d.Mode,
        ModeLabel = ModoAtividade.Rotulo(d.Mode),
        LocationId = d.Mode == ModoAtividade.Presencial ? d.LocationId ?? s.LocationId : null,
        LocationName = d.Mode == ModoAtividade.Presencial
            ? d.Location?.Name ?? s.Location?.Name
            : null,
        Notes = d.Notes
    };
}
