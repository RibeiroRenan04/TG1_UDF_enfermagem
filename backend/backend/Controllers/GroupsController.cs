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
public class GroupsController(AppDbContext db, ConflitoTurmasService conflitos) : ControllerBase
{
    private static readonly string[] TurnosValidos = ["manha", "tarde", "noite"];
    private static readonly string[] AtividadesValidas = ["gestao", "pic", "assistencia", "outro"];

    private sealed record ErroValidacao(
        string Mensagem,
        string? Campo = null,
        int Status = StatusCodes.Status400BadRequest,
        string? Codigo = null);

    [HttpGet]
    public async Task<ActionResult<List<GroupDto>>> GetAll()
    {
        // A contagem sai do banco; antes cada vínculo de cada turma era carregado só para ser contado.
        return Ok(await db.StudentGroups.AsNoTracking()
            .OrderBy(g => g.Code)
            .Select(g => new GroupDto
            {
                Id = g.Id, Code = g.Code, Name = g.Name, Description = g.Description,
                MemberCount = g.Memberships.Count
            })
            .ToListAsync());
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

    /// <summary>Não mexe nas outras turmas do aluno e repetir não duplica. Só a agenda impossível é recusada.</summary>
    [HttpPost("{id}/members/{studentId}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> AddMember(Guid id, Guid studentId)
    {
        if (!await db.StudentGroups.AnyAsync(g => g.Id == id))
            return NotFound(new { message = "Turma não encontrada." });

        var aluno = await db.Users.FirstOrDefaultAsync(u => u.Id == studentId);
        if (aluno == null) return NotFound(new { message = "Aluno não encontrado." });
        if (aluno.Role != Roles.Aluno)
            return BadRequest(ErrosApi.Corpo("Apenas alunos podem ser vinculados a uma turma."));

        if (await db.GroupMemberships.AnyAsync(m => m.StudentId == studentId && m.GroupId == id))
            return NoContent();

        var conflito = await conflitos.VerificarAsync(studentId, id);
        if (conflito != null)
            return Conflict(ErrosApi.Corpo(conflito, "groupId", "conflito_agenda"));

        db.GroupMemberships.Add(new GroupMembership { StudentId = studentId, GroupId = id });
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Vincula e desvincula em lote: o que é válido é gravado junto e cada recusa volta
    /// com o motivo. As outras turmas dos alunos não são tocadas.
    /// </summary>
    [HttpPut("{id}/members")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<VinculoLoteResultadoDto>> AtualizarMembros(Guid id, [FromBody] VinculoLoteDto dto)
    {
        if (!await db.StudentGroups.AnyAsync(g => g.Id == id))
            return NotFound(new { message = "Turma não encontrada." });

        var adicionar = (dto.Adicionar ?? []).Distinct().ToList();
        // Quem aparece nas duas listas fica: marcar vence desmarcar.
        var remover = (dto.Remover ?? []).Distinct().Where(r => !adicionar.Contains(r)).ToList();

        var recusados = new List<VinculoRecusadoDto>();

        var alunos = await db.Users
            .Where(u => adicionar.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Role })
            .ToDictionaryAsync(u => u.Id);

        foreach (var idAluno in adicionar.Where(a => !alunos.ContainsKey(a)))
            recusados.Add(new VinculoRecusadoDto(idAluno, null, "Aluno não encontrado."));
        foreach (var a in alunos.Values.Where(a => a.Role != Roles.Aluno))
            recusados.Add(new VinculoRecusadoDto(a.Id, a.FullName, "Apenas alunos podem ser vinculados a uma turma."));

        var jaVinculados = (await db.GroupMemberships
                .Where(m => m.GroupId == id && (adicionar.Contains(m.StudentId) || remover.Contains(m.StudentId)))
                .ToListAsync())
            .ToDictionary(m => m.StudentId);

        var candidatos = alunos.Values
            .Where(a => a.Role == Roles.Aluno && !jaVinculados.ContainsKey(a.Id))
            .Select(a => a.Id)
            .ToList();

        var conflitosAgenda = await conflitos.VerificarLoteAsync(id, candidatos);
        foreach (var (idAluno, motivo) in conflitosAgenda)
            recusados.Add(new VinculoRecusadoDto(idAluno, alunos[idAluno].FullName, motivo));

        var vinculados = 0;
        foreach (var idAluno in candidatos.Where(c => !conflitosAgenda.ContainsKey(c)))
        {
            db.GroupMemberships.Add(new GroupMembership { StudentId = idAluno, GroupId = id });
            vinculados++;
        }

        var desvinculados = 0;
        foreach (var idAluno in remover)
        {
            if (!jaVinculados.TryGetValue(idAluno, out var vinculo)) continue;
            db.GroupMemberships.Remove(vinculo);
            desvinculados++;
        }

        await db.SaveChangesAsync();

        return Ok(new VinculoLoteResultadoDto(vinculados, desvinculados,
            [.. recusados.OrderBy(r => r.Nome, StringComparer.CurrentCultureIgnoreCase)]));
    }

    [HttpDelete("{id}/members/{studentId}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid studentId)
    {
        var vinculo = await db.GroupMemberships
            .FirstOrDefaultAsync(m => m.StudentId == studentId && m.GroupId == id);

        // Sem vínculo, o estado já é o desejado — repetir a remoção não é erro.
        if (vinculo == null) return NoContent();

        db.GroupMemberships.Remove(vinculo);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("schedules")]
    public async Task<ActionResult<List<ScheduleDto>>> GetSchedules()
    {
        var schedules = await db.RotationSchedules.AsNoTracking()
            .Include(s => s.Group)
            .Include(s => s.Location)
            .Include(s => s.Preceptor)
            .Include(s => s.Days).ThenInclude(d => d.Location)
            .OrderBy(s => s.StartDate)
            .ToListAsync();

        return Ok(schedules.Select(MapSchedule));
    }

    [HttpGet("{groupId}/schedules")]
    public async Task<ActionResult<List<ScheduleDto>>> GetSchedulesByGroup(Guid groupId)
    {
        var schedules = await db.RotationSchedules.AsNoTracking()
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
        AplicarDias(schedule, await ComSextaNaUdfAsync(dto.Days));
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

        // A programação semanal é reconciliada dia a dia: enviar a lista vazia
        // volta o rodízio ao padrão (todo dia útil presencial no local principal).
        ReconciliarDias(schedule, await ComSextaNaUdfAsync(dto.Days));

        await SalvarProgramacaoAsync();

        return Ok(MapSchedule(await RecarregarAsync(id)));
    }

    [HttpDelete("schedules/{id}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> DeleteSchedule(Guid id)
    {
        var schedule = await db.RotationSchedules.FindAsync(id);
        if (schedule == null) return NotFound(new { message = "Rodízio não encontrado. Ele pode ter sido excluído." });
        db.RotationSchedules.Remove(schedule);
        await SalvarProgramacaoAsync();
        return NoContent();
    }

    private ObjectResult Falha(ErroValidacao erro) =>
        StatusCode(erro.Status, ErrosApi.Corpo(erro.Mensagem, erro.Campo, erro.Codigo));

    private async Task<ErroValidacao?> ValidarAlocacaoAsync(CreateScheduleDto dto, Guid? scheduleIdAtual = null)
    {
        var group = await db.StudentGroups
            .Include(g => g.Memberships)
            .FirstOrDefaultAsync(g => g.Id == dto.GroupId);

        if (group == null)
            return new("Turma não encontrada.", "groupId");

        if (group.Memberships.Count == 0)
            return new($"A turma {group.Code} não possui alunos vinculados. "
                     + "Vincule os alunos à turma antes de alocar o rodízio.", "groupId");

        if (!await db.Locations.AnyAsync(l => l.Id == dto.LocationId))
            return new("Local de estágio não encontrado.", "locationId");

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

    /// <summary>Data vazia chega como 01/01/0001 (<c>[Required]</c> não pega <c>DateOnly</c>).</summary>
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

            if (modo == ModoAtividade.Presencial && dia.DayOfWeek == DiasSemana.Sexta
                && await LocalDaSextaAsync() == null)
                return new("Às sextas o estágio presencial é na UDF, mas nenhuma unidade está marcada como "
                         + "instituição de ensino. Marque-a em Unidades de saúde ou deixe a sexta como remota.", "days");

            if (modo == ModoAtividade.Presencial && dia.LocationId.HasValue
                && !await db.Locations.AnyAsync(l => l.Id == dia.LocationId.Value))
                return new($"Local informado em {DiasSemana.Rotulo(dia.DayOfWeek)} não foi encontrado.", "days");
        }

        return null;
    }

    /// <summary>Unidade das sextas: a instituição de ensino (UDF — Laboratórios de Enfermagem).</summary>
    private Task<Location?> LocalDaSextaAsync() =>
        db.Locations
            .Where(l => l.IsInstitution && l.Ativo)
            .OrderBy(l => l.Name)
            .FirstOrDefaultAsync();

    /// <summary>Sexta presencial é sempre na UDF, seja qual for o local enviado.</summary>
    private async Task<List<CriarDiaRodizioDto>?> ComSextaNaUdfAsync(List<CriarDiaRodizioDto>? dias)
    {
        if (dias == null) return null;
        var udf = await LocalDaSextaAsync();
        return [.. dias.Select(d => d.DayOfWeek == DiasSemana.Sexta
                                    && ModoAtividade.Normalizar(d.Mode) == ModoAtividade.Presencial && udf != null
            ? d with { LocationId = udf.Id }
            : d)];
    }

    /// <summary>O local só é guardado no dia presencial.</summary>
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

    /// <summary>
    /// Atualiza, remove e insere só o que mudou. Apagar e reinserir tudo fazia o clique
    /// duplo acusar edição concorrente (o EF trata "apaguei zero linhas" como conflito).
    /// </summary>
    private void ReconciliarDias(RotationSchedule schedule, List<CriarDiaRodizioDto>? dias)
    {
        var desejados = (dias ?? [])
            .GroupBy(d => d.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var atual in schedule.Days.ToList())
        {
            if (!desejados.TryGetValue(atual.DayOfWeek, out var dia))
            {
                db.RotationDaySchedules.Remove(atual);
                schedule.Days.Remove(atual);
                continue;
            }

            var modo = ModoAtividade.Normalizar(dia.Mode) ?? ModoAtividade.Presencial;
            atual.Mode = modo;
            atual.LocationId = modo == ModoAtividade.Presencial ? dia.LocationId : null;
            atual.Notes = string.IsNullOrWhiteSpace(dia.Notes) ? null : dia.Notes.Trim();
            desejados.Remove(atual.DayOfWeek);
        }

        AplicarDias(schedule, [.. desejados.Values]);
    }

    /// <summary>
    /// Sem token de versão, a única <see cref="DbUpdateConcurrencyException"/> possível é um
    /// DELETE de linha já removida: o estado desejado já vale, então gravamos o resto.
    /// </summary>
    private async Task SalvarProgramacaoAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
            when (ex.Entries.All(e => e.State == EntityState.Deleted))
        {
            foreach (var entry in ex.Entries)
                entry.State = EntityState.Detached;

            await db.SaveChangesAsync();
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

    /// <summary>Sem local próprio, o dia herda o local principal do rodízio.</summary>
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
