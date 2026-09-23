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
/// Presença remota por código. O código sozinho não basta: o registro confere grupo,
/// janela de horário, atividade em aberto, participação única e, se exigida, a tarefa.
/// </summary>
[ApiController]
[Route("api/atividades-remotas")]
[Authorize]
public class AtividadesRemotasController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<List<AtividadeRemotaDto>>> GetAll(
        [FromQuery] Guid? groupId,
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        [FromQuery] bool? ativo)
    {
        var query = db.RemoteActivities
            .Include(a => a.Group).ThenInclude(g => g.Memberships)
            .Include(a => a.Schedule)
            .Include(a => a.Professor)
            .Include(a => a.Participations)
            .AsQueryable();

        if (groupId.HasValue) query = query.Where(a => a.GroupId == groupId.Value);
        if (de.HasValue) query = query.Where(a => a.ActivityDate >= de.Value);
        if (ate.HasValue) query = query.Where(a => a.ActivityDate <= ate.Value);
        if (ativo.HasValue) query = query.Where(a => a.Ativo == ativo.Value);

        var atividades = await query
            .OrderByDescending(a => a.ActivityDate)
            .ThenBy(a => a.StartTime)
            .ToListAsync();

        return Ok(atividades.Select(Map));
    }

    [HttpGet("{id}")]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<AtividadeRemotaDto>> Get(Guid id)
    {
        var atividade = await CarregarAsync(id);
        return atividade == null ? NotFound() : Ok(Map(atividade));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<AtividadeRemotaDto>> Create([FromBody] CriarAtividadeRemotaDto dto)
    {
        var erro = await ValidarAsync(dto);
        if (erro is { } e) return BadRequest(ErrosApi.Corpo(e.Mensagem, e.Campo));

        var atividade = new RemoteActivity
        {
            Title = dto.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            GroupId = dto.GroupId,
            ScheduleId = dto.ScheduleId,
            ProfessorId = UsuarioAtual(),
            ActivityDate = dto.ActivityDate,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            EstimatedHours = dto.EstimatedHours,
            PresenceCode = await GerarCodigoUnicoAsync(dto.ActivityDate),
            RequiresTask = dto.RequiresTask,
            TaskType = dto.RequiresTask ? dto.TaskType : null,
            TaskInstructions = dto.RequiresTask && !string.IsNullOrWhiteSpace(dto.TaskInstructions)
                ? dto.TaskInstructions.Trim()
                : null
        };

        db.RemoteActivities.Add(atividade);
        await db.SaveChangesAsync();

        return Ok(Map((await CarregarAsync(atividade.Id))!));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<AtividadeRemotaDto>> Update(Guid id, [FromBody] CriarAtividadeRemotaDto dto)
    {
        var atividade = await db.RemoteActivities
            .Include(a => a.Participations)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (atividade == null) return NotFound();

        // Com presenças registradas, mudar grupo, data ou janela invalidaria participações corretas.
        if (atividade.Participations.Count > 0 &&
            (atividade.GroupId != dto.GroupId || atividade.ActivityDate != dto.ActivityDate))
            return Conflict(new
            {
                message = "A atividade já possui participações registradas: a turma e a data não "
                        + "podem mais ser alteradas. Encerre esta atividade e crie outra.",
                code = "atividade_com_participacoes"
            });

        var erro = await ValidarAsync(dto);
        if (erro is { } e) return BadRequest(ErrosApi.Corpo(e.Mensagem, e.Campo));

        atividade.Title = dto.Title.Trim();
        atividade.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        atividade.GroupId = dto.GroupId;
        atividade.ScheduleId = dto.ScheduleId;
        atividade.ActivityDate = dto.ActivityDate;
        atividade.StartTime = dto.StartTime;
        atividade.EndTime = dto.EndTime;
        atividade.EstimatedHours = dto.EstimatedHours;
        atividade.RequiresTask = dto.RequiresTask;
        atividade.TaskType = dto.RequiresTask ? dto.TaskType : null;
        atividade.TaskInstructions = dto.RequiresTask && !string.IsNullOrWhiteSpace(dto.TaskInstructions)
            ? dto.TaskInstructions.Trim()
            : null;
        atividade.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();
        return Ok(Map((await CarregarAsync(id))!));
    }

    /// <summary>O código deixa de valer na hora; as participações já registradas permanecem.</summary>
    [HttpPatch("{id}/encerrar")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<AtividadeRemotaDto>> Encerrar(Guid id)
    {
        var atividade = await db.RemoteActivities.FirstOrDefaultAsync(a => a.Id == id);
        if (atividade == null) return NotFound();

        atividade.Ativo = false;
        atividade.UpdatedAt = BrasiliaTime.Agora;
        await db.SaveChangesAsync();

        return Ok(Map((await CarregarAsync(id))!));
    }

    /// <summary>Para quando o código vazou para alunos de fora do grupo.</summary>
    [HttpPatch("{id}/novo-codigo")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<AtividadeRemotaDto>> NovoCodigo(Guid id)
    {
        var atividade = await db.RemoteActivities.FirstOrDefaultAsync(a => a.Id == id);
        if (atividade == null) return NotFound();

        atividade.PresenceCode = await GerarCodigoUnicoAsync(atividade.ActivityDate);
        atividade.UpdatedAt = BrasiliaTime.Agora;
        await db.SaveChangesAsync();

        return Ok(Map((await CarregarAsync(id))!));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var atividade = await db.RemoteActivities
            .Include(a => a.Participations)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (atividade == null) return NotFound();

        // Excluir apagaria a presença de quem já participou: nesse caso só encerra.
        if (atividade.Participations.Count > 0)
            return Conflict(new
            {
                message = "A atividade possui participações registradas e não pode ser excluída. "
                        + "Use \"Encerrar\" para invalidar o código.",
                code = "atividade_com_participacoes"
            });

        db.RemoteActivities.Remove(atividade);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id}/participacoes")]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<List<ParticipacaoAtividadeDto>>> GetParticipacoes(Guid id)
    {
        if (!await db.RemoteActivities.AnyAsync(a => a.Id == id))
            return NotFound(new { message = "Atividade não encontrada." });

        var participacoes = await db.RemoteActivityParticipations
            .Include(p => p.Student)
            .Where(p => p.RemoteActivityId == id)
            .OrderBy(p => p.Student.FullName)
            .Select(p => new ParticipacaoAtividadeDto
            {
                Id = p.Id,
                RemoteActivityId = p.RemoteActivityId,
                StudentId = p.StudentId,
                StudentName = p.Student.FullName,
                StudentRgm = p.Student.Rgm,
                RegisteredAt = p.RegisteredAt,
                TaskResponse = p.TaskResponse
            })
            .ToListAsync();

        return Ok(participacoes);
    }

    /// <summary>Não traz o código: o professor o entrega à parte.</summary>
    [HttpGet("minhas")]
    public async Task<ActionResult<List<AtividadeRemotaAlunoDto>>> Minhas(
        [FromQuery] DateOnly? de, [FromQuery] DateOnly? ate)
    {
        var userId = UsuarioAtual();
        var turmas = await TurmasDoAlunoAsync(userId);
        if (turmas.Count == 0) return Ok(new List<AtividadeRemotaAlunoDto>());

        var inicio = de ?? BrasiliaTime.Hoje.AddDays(-30);
        var fim = ate ?? BrasiliaTime.Hoje.AddDays(30);

        var atividades = await db.RemoteActivities
            .Where(a => turmas.Contains(a.GroupId)
                     && a.ActivityDate >= inicio && a.ActivityDate <= fim)
            .OrderByDescending(a => a.ActivityDate)
            .ThenBy(a => a.StartTime)
            .ToListAsync();

        var participacoes = await ParticipacoesDoAlunoAsync(userId, atividades.Select(a => a.Id).ToList());

        return Ok(atividades.Select(a => MapParaAluno(a, participacoes.GetValueOrDefault(a.Id))));
    }

    /// <summary>Cada recusa explica o motivo (código errado, prazo encerrado, grupo de outro aluno).</summary>
    [HttpPost("registrar-presenca")]
    [Authorize(Roles = Roles.Aluno)]
    public async Task<ActionResult<PresencaRemotaResultadoDto>> RegistrarPresenca(
        [FromBody] RegistrarPresencaRemotaDto dto)
    {
        var userId = UsuarioAtual();
        var agora = BrasiliaTime.Agora;
        var codigo = RemoteActivity.NormalizarCodigo(dto.Code);

        if (codigo.Length == 0)
            return BadRequest(new { message = "Informe o código de presença.", code = "codigo_vazio" });

        var turmas = await TurmasDoAlunoAsync(userId);
        if (turmas.Count == 0)
            return BadRequest(new
            {
                message = "Você não está vinculado a nenhuma turma. Procure a coordenação.",
                code = "sem_turma"
            });

        // Compara o código normalizado: o aluno pode digitar sem hífen ou em minúsculas.
        var candidatas = await db.RemoteActivities
            .Where(a => a.ActivityDate >= BrasiliaTime.Hoje.AddDays(-1)
                     && a.ActivityDate <= BrasiliaTime.Hoje.AddDays(1))
            .ToListAsync();

        var atividades = candidatas
            .Where(a => RemoteActivity.NormalizarCodigo(a.PresenceCode) == codigo)
            .ToList();

        if (atividades.Count == 0)
            return NotFound(new
            {
                message = "Código não encontrado. Confira o código informado pelo professor.",
                code = "codigo_invalido"
            });

        var atividade = atividades.FirstOrDefault(a => turmas.Contains(a.GroupId));
        if (atividade == null)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Este código pertence a uma atividade de outro grupo. "
                        + "Você não pode registrar presença nela.",
                code = "grupo_nao_autorizado"
            });

        if (!atividade.Ativo)
            return BadRequest(new
            {
                message = "Esta atividade foi encerrada pelo professor e o código não vale mais.",
                code = "atividade_encerrada"
            });

        if (agora < atividade.InicioEm)
            return BadRequest(new
            {
                message = $"A atividade \"{atividade.Title}\" começa em "
                        + $"{atividade.InicioEm:dd/MM 'às' HH\\:mm}. O código ainda não vale.",
                code = "fora_do_prazo"
            });

        if (agora > atividade.FimEm)
            return BadRequest(new
            {
                message = $"O prazo da atividade \"{atividade.Title}\" encerrou em "
                        + $"{atividade.FimEm:dd/MM 'às' HH\\:mm}. Registre uma irregularidade se houver motivo.",
                code = "fora_do_prazo"
            });

        var jaRegistrou = await db.RemoteActivityParticipations
            .FirstOrDefaultAsync(p => p.RemoteActivityId == atividade.Id && p.StudentId == userId);
        if (jaRegistrou != null)
            return Conflict(new
            {
                message = $"Você já registrou participação nesta atividade em "
                        + $"{jaRegistrou.RegisteredAt:dd/MM 'às' HH\\:mm}. O código vale uma vez só.",
                code = "participacao_duplicada"
            });

        var resposta = dto.TaskResponse?.Trim();
        if (atividade.RequiresTask && TipoTarefaRemota.ExigeResposta(atividade.TaskType)
            && string.IsNullOrWhiteSpace(resposta))
            return BadRequest(new
            {
                message = "Esta atividade exige a entrega da tarefa junto com o código.",
                code = "tarefa_obrigatoria",
                taskType = atividade.TaskType
            });

        // O ponto remoto vai para a mesma tabela do presencial, para contar no mesmo cálculo de horas.
        var (entrada, saida) = MontarPonto(atividade, userId, resposta);
        db.AttendanceRecords.Add(entrada);
        db.AttendanceRecords.Add(saida);

        var participacao = new RemoteActivityParticipation
        {
            RemoteActivityId = atividade.Id,
            StudentId = userId,
            RegisteredAt = agora,
            CodeUsed = dto.Code.Trim().ToUpperInvariant(),
            TaskResponse = string.IsNullOrWhiteSpace(resposta) ? null : resposta,
            AttendanceRecordId = entrada.Id
        };

        db.RemoteActivityParticipations.Add(participacao);
        await db.SaveChangesAsync();

        return Ok(new PresencaRemotaResultadoDto
        {
            ParticipationId = participacao.Id,
            RemoteActivityId = atividade.Id,
            ActivityTitle = atividade.Title,
            RegisteredAt = participacao.RegisteredAt,
            HorasCreditadas = atividade.EstimatedHours,
            Message = $"Participação registrada em \"{atividade.Title}\". "
                    + $"{atividade.EstimatedHours:0.#} h creditadas."
        });
    }

    /// <summary>A saída fica na carga horária informada, não no relógio: a atividade é avaliada pela entrega.</summary>
    private static (AttendanceRecord entrada, AttendanceRecord saida) MontarPonto(
        RemoteActivity atividade, Guid studentId, string? resposta)
    {
        var inicio = atividade.InicioEm;
        var horas = atividade.EstimatedHours > 0
            ? atividade.EstimatedHours
            : (atividade.FimEm - inicio).TotalHours;
        var fim = inicio.AddHours(horas);

        var descricao = $"Atividade remota: {atividade.Title}."
            + (string.IsNullOrWhiteSpace(resposta) ? string.Empty : $" Entrega: {resposta}");

        AttendanceRecord Registro(string tipo, DateTime momento) => new()
        {
            StudentId = studentId,
            ScheduleId = atividade.ScheduleId,
            LocationId = null,
            RemoteActivityId = atividade.Id,
            Type = tipo,
            RecordedAt = momento,
            Latitude = 0,
            Longitude = 0,
            ActivitiesDescription = tipo == "check_out" ? descricao : null,
            Status = "aprovado"
        };

        return (Registro("check_in", inicio), Registro("check_out", fim));
    }

    private async Task<(string Mensagem, string Campo)?> ValidarAsync(CriarAtividadeRemotaDto dto)
    {
        var grupo = await db.StudentGroups
            .Include(g => g.Memberships)
            .FirstOrDefaultAsync(g => g.Id == dto.GroupId);

        if (grupo == null) return ("Turma ou grupo não encontrado.", "groupId");
        if (grupo.Memberships.Count == 0)
            return ($"A turma {grupo.Code} não possui alunos vinculados. "
                  + "Vincule os alunos antes de criar a atividade.", "groupId");

        if (dto.ScheduleId.HasValue)
        {
            var escala = await db.RotationSchedules
                .FirstOrDefaultAsync(s => s.Id == dto.ScheduleId.Value);
            if (escala == null) return ("Rodízio não encontrado.", "scheduleId");
            if (escala.GroupId != dto.GroupId)
                return ("O rodízio informado é de outra turma.", "scheduleId");
        }

        // Campo de data vazio chega como 01/01/0001: o [Required] não pega DateOnly.
        if (dto.ActivityDate == default)
            return ("Informe a data da atividade.", "activityDate");

        if (dto.EndTime <= dto.StartTime)
            return ("O horário de término precisa ser posterior ao de início.", "endTime");

        if (dto.EstimatedHours <= 0)
            return ("Informe a carga horária da atividade.", "estimatedHours");

        var janela = (dto.EndTime - dto.StartTime).TotalHours;
        if (dto.EstimatedHours > janela + 0.001)
            return ($"A carga horária ({dto.EstimatedHours:0.#} h) não pode ser maior que a "
                  + $"janela da atividade ({janela:0.#} h).", "estimatedHours");

        if (dto.RequiresTask && !TipoTarefaRemota.Valido(dto.TaskType))
            return ("Selecione o tipo da tarefa complementar.", "taskType");

        if (dto.RequiresTask && string.IsNullOrWhiteSpace(dto.TaskInstructions))
            return ("O campo 'Instruções da tarefa' é obrigatório quando a tarefa complementar está ativa.",
                "taskInstructions");

        return null;
    }

    private async Task<string> GerarCodigoUnicoAsync(DateOnly data)
    {
        for (var tentativa = 0; tentativa < 20; tentativa++)
        {
            var codigo = RemoteActivity.GerarCodigo();
            if (!await db.RemoteActivities.AnyAsync(a => a.ActivityDate == data && a.PresenceCode == codigo))
                return codigo;
        }

        // Fim de linha improvável; o sufixo do relógio remove qualquer colisão.
        return $"{RemoteActivity.GerarCodigo()}{BrasiliaTime.Agora:ss}";
    }

    private async Task<RemoteActivity?> CarregarAsync(Guid id) =>
        await db.RemoteActivities
            .Include(a => a.Group).ThenInclude(g => g.Memberships)
            .Include(a => a.Schedule)
            .Include(a => a.Professor)
            .Include(a => a.Participations)
            .FirstOrDefaultAsync(a => a.Id == id);

    private async Task<Dictionary<Guid, RemoteActivityParticipation>> ParticipacoesDoAlunoAsync(
        Guid studentId, List<Guid> atividadeIds)
    {
        if (atividadeIds.Count == 0) return [];

        return await db.RemoteActivityParticipations
            .Where(p => p.StudentId == studentId && atividadeIds.Contains(p.RemoteActivityId))
            .ToDictionaryAsync(p => p.RemoteActivityId);
    }

    private async Task<List<Guid>> TurmasDoAlunoAsync(Guid studentId) =>
        await db.GroupMemberships
            .Where(m => m.StudentId == studentId)
            .Select(m => m.GroupId)
            .Distinct()
            .ToListAsync();

    private Guid UsuarioAtual() => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    internal static string Situacao(RemoteActivity a)
    {
        var agora = BrasiliaTime.Agora;
        if (!a.Ativo || agora > a.FimEm) return "encerrada";
        return agora < a.InicioEm ? "agendada" : "aberta";
    }

    private static AtividadeRemotaDto Map(RemoteActivity a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Description = a.Description,
        GroupId = a.GroupId,
        GroupCode = a.Group?.Code ?? string.Empty,
        GroupName = a.Group?.Name,
        ScheduleId = a.ScheduleId,
        PeriodLabel = a.Schedule?.PeriodLabel,
        ProfessorId = a.ProfessorId,
        ProfessorName = a.Professor?.FullName ?? string.Empty,
        ActivityDate = a.ActivityDate,
        StartTime = a.StartTime,
        EndTime = a.EndTime,
        EstimatedHours = a.EstimatedHours,
        PresenceCode = a.PresenceCode,
        RequiresTask = a.RequiresTask,
        TaskType = a.TaskType,
        TaskTypeLabel = a.TaskType == null ? null : TipoTarefaRemota.Rotulo(a.TaskType),
        TaskInstructions = a.TaskInstructions,
        Ativo = a.Ativo,
        Aberta = a.AbertaEm(BrasiliaTime.Agora),
        Situacao = Situacao(a),
        TotalParticipantes = a.Participations?.Count ?? 0,
        TotalAlunosGrupo = a.Group?.Memberships?.Count ?? 0,
        CreatedAt = a.CreatedAt
    };

    internal static AtividadeRemotaAlunoDto MapParaAluno(
        RemoteActivity a, RemoteActivityParticipation? participacao) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Description = a.Description,
        ActivityDate = a.ActivityDate,
        StartTime = a.StartTime,
        EndTime = a.EndTime,
        EstimatedHours = a.EstimatedHours,
        RequiresTask = a.RequiresTask,
        TaskType = a.TaskType,
        TaskTypeLabel = a.TaskType == null ? null : TipoTarefaRemota.Rotulo(a.TaskType),
        TaskInstructions = a.TaskInstructions,
        Aberta = a.AbertaEm(BrasiliaTime.Agora),
        Situacao = Situacao(a),
        JaRegistrada = participacao != null,
        RegistradaEm = participacao?.RegisteredAt
    };
}
