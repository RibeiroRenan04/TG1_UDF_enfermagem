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
/// Alocação de estagiários às unidades de saúde.
///
/// Só usuários com papel "aluno" podem ser alocados. A alocação é <b>por turno</b>:
/// o mesmo aluno pode estagiar de manhã em uma unidade e à tarde em outra, mas
/// nunca ter duas alocações ativas no mesmo turno — a regra vale na API e no
/// índice único do banco.
///
/// Trocar de unidade encerra a alocação daquele turno e cria outra: o histórico é
/// preservado, nunca sobrescrito.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class AlocacoesController(AppDbContext db, ILogger<AlocacoesController> logger) : ControllerBase
{
    // ── Estagiários de uma unidade ────────────────────────────────────────────
    /// <summary>
    /// Estagiários alocados na unidade. O preceptor e a gestão veem a lista toda;
    /// o aluno consulta a unidade (precisa saber onde estagia), mas só enxerga a
    /// própria alocação — a relação dos colegas não é dado dele.
    /// </summary>
    [HttpGet("unidades-saude/{id}/estagiarios")]
    public async Task<ActionResult<List<AlocacaoDto>>> GetEstagiariosDaUnidade(
        Guid id, [FromQuery] bool incluirEncerradas = false)
    {
        if (!await db.Locations.AnyAsync(l => l.Id == id))
            return NotFound(new { message = "Unidade não encontrada." });

        var query = db.StudentAllocations
            .Include(a => a.Student)
            .Include(a => a.Location)
            .Include(a => a.CreatedBy)
            .Where(a => a.LocationId == id);

        if (!incluirEncerradas) query = query.Where(a => a.Ativo);

        if ((User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno) == Roles.Aluno)
        {
            var eu = UsuarioAtual();
            query = query.Where(a => a.StudentId == eu);
        }

        var alocacoes = await query
            .OrderByDescending(a => a.Ativo)
            .ThenBy(a => a.Student.FullName)
            .ThenBy(a => a.Shift)
            .ToListAsync();

        return Ok(alocacoes.Select(Map));
    }

    /// <summary>
    /// Alunos que podem ser alocados nesta unidade. Traz a unidade atual de cada um
    /// para que a tela avise antes de uma troca acidental.
    /// </summary>
    [HttpGet("unidades-saude/{id}/estagiarios-disponiveis")]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<List<EstagiarioDisponivelDto>>> GetDisponiveis(
        Guid id, [FromQuery] string? busca)
    {
        var query = db.Users
            .Include(u => u.GroupMembership).ThenInclude(m => m!.Group)
            .Where(u => u.Role == Roles.Aluno && u.IsActive);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            query = query.Where(u =>
                EF.Functions.ILike(u.FullName, $"%{termo}%") ||
                (u.Rgm != null && u.Rgm.Contains(termo)));
        }

        var alunos = await query.OrderBy(u => u.FullName).Take(100).ToListAsync();

        var alunoIds = alunos.Select(u => u.Id).ToList();

        // Um aluno pode ter várias alocações ativas — uma por turno.
        var ativas = await db.StudentAllocations
            .Include(a => a.Location)
            .Where(a => a.Ativo && alunoIds.Contains(a.StudentId))
            .ToListAsync();

        var porAluno = ativas
            .GroupBy(a => a.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return Ok(alunos.Select(u =>
        {
            var minhas = porAluno.GetValueOrDefault(u.Id, []);
            var ocupados = minhas.Select(a => a.Shift).ToHashSet();
            // A unidade "atual" continua sendo exibida: é a do turno cadastrado do
            // aluno, ou a primeira que ele tiver.
            var principal = minhas.FirstOrDefault(a => a.Shift == Turnos.Normalizar(u.Shift))
                         ?? minhas.FirstOrDefault();

            return new EstagiarioDisponivelDto
            {
                Id = u.Id,
                Nome = u.FullName,
                Rgm = u.Rgm,
                Email = u.Email,
                Semestre = u.Semester,
                Turno = u.Shift,
                Turma = u.GroupMembership?.Group?.Code,
                UnidadeAtualId = principal?.LocationId,
                UnidadeAtualNome = principal?.Location?.Name,
                AlocacoesAtivas = [.. minhas.Select(a => new AlocacaoPorTurnoDto
                {
                    Turno = a.Shift,
                    UnidadeId = a.LocationId,
                    UnidadeNome = a.Location?.Name ?? string.Empty
                })],
                TurnosDisponiveis = [.. Turnos.Validos.Where(t => !ocupados.Contains(t))]
            };
        }));
    }

    // ── Criar alocação ────────────────────────────────────────────────────────
    [HttpPost("unidades-saude/{id}/estagiarios")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<AlocacaoDto>> Alocar(Guid id, [FromBody] CriarAlocacaoDto dto)
    {
        var unidade = await db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (unidade == null) return NotFound(new { message = "Unidade não encontrada." });
        if (!unidade.Ativo)
            return BadRequest(new { message = "Unidade inativa não recebe novas alocações." });

        var aluno = await db.Users.FirstOrDefaultAsync(u => u.Id == dto.EstagiarioId);
        if (aluno == null) return NotFound(new { message = "Estagiário não encontrado." });

        // Regra validada aqui, não só na tela: só aluno estagia.
        if (aluno.Role != Roles.Aluno)
            return BadRequest(new
            {
                message = $"Apenas usuários com perfil de aluno podem ser alocados como estagiários. "
                        + $"\"{aluno.FullName}\" tem o perfil {aluno.Role}."
            });

        if (!aluno.IsActive)
            return BadRequest(new { message = "Estagiário inativo não pode ser alocado." });

        var inicio = dto.DataInicio ?? BrasiliaTime.Hoje;

        // O turno é a chave da alocação. Sem turno informado, vale o do cadastro do
        // aluno; sem esse também, a manhã.
        var turnoInformado = Turnos.Normalizar(dto.Turno);
        if (!string.IsNullOrWhiteSpace(dto.Turno) && turnoInformado == null)
            return BadRequest(new { message = "Turno inválido. Use manhã, tarde ou noite." });

        var turno = turnoInformado ?? Turnos.Normalizar(aluno.Shift) ?? Turnos.Manha;

        // Alocações em OUTROS turnos convivem: o aluno pode estagiar de manhã em uma
        // unidade e à tarde em outra. A trava vale só para o mesmo turno.
        var alocacaoDoTurno = await db.StudentAllocations
            .Include(a => a.Location)
            .FirstOrDefaultAsync(a => a.StudentId == aluno.Id && a.Ativo && a.Shift == turno);

        if (alocacaoDoTurno != null)
        {
            if (alocacaoDoTurno.LocationId == id)
                return Conflict(new
                {
                    message = $"{aluno.FullName} já está alocado(a) nesta unidade no turno da "
                            + $"{Turnos.Rotulo(turno)}.",
                    code = "alocacao_duplicada_no_turno",
                    turno
                });

            // Trocar de unidade precisa ser explícito: encerra a anterior daquele
            // turno e abre outra, mantendo o histórico.
            if (!dto.EncerrarAlocacaoAtual)
                return Conflict(new
                {
                    message = $"{aluno.FullName} já está alocado(a) em \"{alocacaoDoTurno.Location.Name}\" "
                            + $"no turno da {Turnos.Rotulo(turno)}. "
                            + "Encerre a alocação desse turno para transferir, ou escolha outro turno.",
                    code = "alocacao_ativa_existente",
                    turno,
                    unidadeAtualId = alocacaoDoTurno.LocationId,
                    unidadeAtualNome = alocacaoDoTurno.Location.Name
                });

            alocacaoDoTurno.Ativo = false;
            alocacaoDoTurno.EndDate = inicio;
            alocacaoDoTurno.UpdatedAt = BrasiliaTime.Agora;

            logger.LogInformation(
                "Alocação de {Aluno} na unidade {Unidade} ({Turno}) encerrada para transferência.",
                aluno.FullName, alocacaoDoTurno.Location.Name, turno);
        }

        var alocacao = new StudentAllocation
        {
            LocationId = id,
            StudentId = aluno.Id,
            Shift = turno,
            StartDate = inicio,
            Ativo = true,
            Observacao = dto.Observacao?.Trim(),
            CreatedById = UsuarioAtual()
        };

        db.StudentAllocations.Add(alocacao);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (alocacaoDoTurno == null)
        {
            // O índice único (estagiário + turno, ativas) é a última barreira contra
            // duas requisições simultâneas para o mesmo turno.
            return Conflict(new
            {
                message = $"{aluno.FullName} já possui uma alocação ativa no turno da "
                        + $"{Turnos.Rotulo(turno)}.",
                code = "alocacao_duplicada_no_turno",
                turno
            });
        }

        await db.Entry(alocacao).Reference(a => a.Student).LoadAsync();
        await db.Entry(alocacao).Reference(a => a.Location).LoadAsync();

        logger.LogInformation("Alocação criada: {Aluno} → {Unidade} ({Turno}).",
            aluno.FullName, unidade.Name, turno);
        return Ok(Map(alocacao));
    }

    // ── Encerrar alocação ─────────────────────────────────────────────────────
    /// <summary>
    /// Encerra a alocação ativa do estagiário na unidade. Com vários turnos por
    /// aluno, <paramref name="turno"/> diz qual encerrar; sem ele, encerra a única
    /// existente e recusa quando houver mais de uma.
    /// </summary>
    [HttpDelete("unidades-saude/{id}/estagiarios/{idEstagiario}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<AlocacaoDto>> Encerrar(
        Guid id, Guid idEstagiario, [FromBody] EncerrarAlocacaoDto? dto, [FromQuery] string? turno = null)
    {
        var ativas = await db.StudentAllocations
            .Include(a => a.Student)
            .Include(a => a.Location)
            .Where(a => a.LocationId == id && a.StudentId == idEstagiario && a.Ativo)
            .ToListAsync();

        if (ativas.Count == 0)
            return NotFound(new { message = "Alocação ativa não encontrada para este estagiário nesta unidade." });

        var turnoAlvo = Turnos.Normalizar(turno);
        if (!string.IsNullOrWhiteSpace(turno) && turnoAlvo == null)
            return BadRequest(new { message = "Turno inválido. Use manhã, tarde ou noite." });

        StudentAllocation? alocacao = turnoAlvo != null
            ? ativas.FirstOrDefault(a => a.Shift == turnoAlvo)
            : ativas.Count == 1 ? ativas[0] : null;

        if (alocacao == null && turnoAlvo != null)
            return NotFound(new
            {
                message = $"Nenhuma alocação ativa no turno da {Turnos.Rotulo(turnoAlvo)} "
                        + "para este estagiário nesta unidade."
            });

        // Sem turno informado e com mais de uma alocação ativa, encerrar qual delas
        // seria um chute: a API pede o turno em vez de escolher por conta própria.
        if (alocacao == null)
            return BadRequest(new
            {
                message = "O estagiário tem mais de uma alocação ativa nesta unidade. "
                        + "Informe o turno que deve ser encerrado.",
                code = "turno_obrigatorio",
                turnos = ativas.Select(a => a.Shift).ToList()
            });

        // Encerrar preserva a linha: é o histórico de onde o aluno esteve.
        alocacao.Ativo = false;
        alocacao.EndDate = dto?.DataFim ?? BrasiliaTime.Hoje;
        if (!string.IsNullOrWhiteSpace(dto?.Observacao))
            alocacao.Observacao = dto.Observacao.Trim();
        alocacao.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Alocação encerrada: {Aluno} deixou a unidade {Unidade} ({Turno}).",
            alocacao.Student.FullName, alocacao.Location.Name, alocacao.Shift);

        return Ok(Map(alocacao));
    }

    // ── Tela geral de alocações ───────────────────────────────────────────────
    [HttpGet("alocacoes")]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<List<AlocacaoDto>>> GetTodas(
        [FromQuery] Guid? unidadeId,
        [FromQuery] Guid? estagiarioId,
        [FromQuery] bool? ativo,
        [FromQuery] string? turno,
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate)
    {
        var query = db.StudentAllocations
            .Include(a => a.Student)
            .Include(a => a.Location)
            .Include(a => a.CreatedBy)
            .AsQueryable();

        if (unidadeId.HasValue) query = query.Where(a => a.LocationId == unidadeId.Value);
        if (estagiarioId.HasValue) query = query.Where(a => a.StudentId == estagiarioId.Value);
        if (ativo.HasValue) query = query.Where(a => a.Ativo == ativo.Value);
        var turnoFiltro = Turnos.Normalizar(turno);
        if (turnoFiltro != null) query = query.Where(a => a.Shift == turnoFiltro);
        if (de.HasValue) query = query.Where(a => a.StartDate >= de.Value);
        if (ate.HasValue) query = query.Where(a => a.StartDate <= ate.Value);

        var alocacoes = await query
            .OrderByDescending(a => a.Ativo)
            .ThenByDescending(a => a.StartDate)
            .ThenBy(a => a.Shift)
            .Take(500)
            .ToListAsync();

        return Ok(alocacoes.Select(Map));
    }

    /// <summary>
    /// Unidade de um estagiário. O aluno só enxerga a própria — e não a altera.
    ///
    /// Com alocação por turno o aluno pode ter mais de uma; sem
    /// <paramref name="turno"/>, devolve a do turno cadastrado dele.
    /// </summary>
    [HttpGet("estagiarios/{id}/unidade")]
    public async Task<ActionResult<AlocacaoDto>> GetUnidadeDoEstagiario(Guid id, [FromQuery] string? turno = null)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;
        if (role == Roles.Aluno && UsuarioAtual() != id)
            return Forbid();

        var ativas = await db.StudentAllocations
            .Include(a => a.Student)
            .Include(a => a.Location)
            .Include(a => a.CreatedBy)
            .Where(a => a.StudentId == id && a.Ativo)
            .ToListAsync();

        if (ativas.Count == 0)
            return NotFound(new { message = "Nenhuma unidade alocada para este estagiário." });

        var turnoAlvo = Turnos.Normalizar(turno)
                     ?? Turnos.Normalizar(ativas[0].Student?.Shift);

        var alocacao = ativas.FirstOrDefault(a => a.Shift == turnoAlvo) ?? ativas[0];

        return Ok(Map(alocacao));
    }

    /// <summary>Todas as alocações ativas do estagiário, uma por turno.</summary>
    [HttpGet("estagiarios/{id}/unidades")]
    public async Task<ActionResult<List<AlocacaoDto>>> GetUnidadesDoEstagiario(Guid id)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;
        if (role == Roles.Aluno && UsuarioAtual() != id)
            return Forbid();

        var ativas = await db.StudentAllocations
            .Include(a => a.Student)
            .Include(a => a.Location)
            .Include(a => a.CreatedBy)
            .Where(a => a.StudentId == id && a.Ativo)
            .OrderBy(a => a.Shift)
            .ToListAsync();

        return Ok(ativas.Select(Map));
    }

    /// <summary>Histórico de alocações de um estagiário.</summary>
    [HttpGet("estagiarios/{id}/alocacoes")]
    public async Task<ActionResult<List<AlocacaoDto>>> GetHistorico(Guid id)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;
        if (role == Roles.Aluno && UsuarioAtual() != id)
            return Forbid();

        var alocacoes = await db.StudentAllocations
            .Include(a => a.Student)
            .Include(a => a.Location)
            .Include(a => a.CreatedBy)
            .Where(a => a.StudentId == id)
            .OrderByDescending(a => a.StartDate)
            .ThenBy(a => a.Shift)
            .ToListAsync();

        return Ok(alocacoes.Select(Map));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Guid UsuarioAtual() => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    private static AlocacaoDto Map(StudentAllocation a) => new()
    {
        Id = a.Id,
        UnidadeId = a.LocationId,
        UnidadeNome = a.Location?.Name ?? string.Empty,
        UnidadeCidade = a.Location?.Cidade,
        EstagiarioId = a.StudentId,
        EstagiarioNome = a.Student?.FullName ?? string.Empty,
        Turno = a.Shift,
        EstagiarioRgm = a.Student?.Rgm,
        EstagiarioEmail = a.Student?.Email,
        EstagiarioSemestre = a.Student?.Semester,
        EstagiarioTurno = a.Student?.Shift,
        DataInicio = a.StartDate,
        DataFim = a.EndDate,
        Ativo = a.Ativo,
        Observacao = a.Observacao,
        CriadoPorNome = a.CreatedBy?.FullName,
        CriadoEm = a.CreatedAt
    };
}
