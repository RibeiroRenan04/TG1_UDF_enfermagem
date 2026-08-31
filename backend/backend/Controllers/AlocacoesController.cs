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
/// Só usuários com papel "aluno" podem ser alocados, e cada um tem no máximo uma
/// alocação ativa. Trocar de unidade encerra a alocação atual e cria outra: o
/// histórico é preservado, nunca sobrescrito.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class AlocacoesController(AppDbContext db, ILogger<AlocacoesController> logger) : ControllerBase
{
    // ── Estagiários de uma unidade ────────────────────────────────────────────
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

        var alocacoes = await query
            .OrderByDescending(a => a.Ativo)
            .ThenBy(a => a.Student.FullName)
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

        var ativas = await db.StudentAllocations
            .Include(a => a.Location)
            .Where(a => a.Ativo)
            .ToDictionaryAsync(a => a.StudentId, a => a.Location);

        return Ok(alunos.Select(u => new EstagiarioDisponivelDto
        {
            Id = u.Id,
            Nome = u.FullName,
            Rgm = u.Rgm,
            Email = u.Email,
            Semestre = u.Semester,
            Turno = u.Shift,
            Turma = u.GroupMembership?.Group?.Code,
            UnidadeAtualId = ativas.TryGetValue(u.Id, out var l) ? l.Id : null,
            UnidadeAtualNome = ativas.TryGetValue(u.Id, out var l2) ? l2.Name : null
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

        var alocacaoAtual = await db.StudentAllocations
            .Include(a => a.Location)
            .FirstOrDefaultAsync(a => a.StudentId == aluno.Id && a.Ativo);

        if (alocacaoAtual != null)
        {
            if (alocacaoAtual.LocationId == id)
                return Conflict(new { message = $"{aluno.FullName} já está alocado(a) nesta unidade." });

            // Trocar de unidade precisa ser explícito: encerra a anterior e abre outra,
            // mantendo o histórico.
            if (!dto.EncerrarAlocacaoAtual)
                return Conflict(new
                {
                    message = $"{aluno.FullName} já está alocado(a) em \"{alocacaoAtual.Location.Name}\". "
                            + "Encerre a alocação atual para transferir.",
                    code = "alocacao_ativa_existente",
                    unidadeAtualId = alocacaoAtual.LocationId,
                    unidadeAtualNome = alocacaoAtual.Location.Name
                });

            alocacaoAtual.Ativo = false;
            alocacaoAtual.EndDate = inicio;
            alocacaoAtual.UpdatedAt = BrasiliaTime.Agora;

            logger.LogInformation(
                "Alocação de {Aluno} na unidade {Unidade} encerrada para transferência.",
                aluno.FullName, alocacaoAtual.Location.Name);
        }

        var alocacao = new StudentAllocation
        {
            LocationId = id,
            StudentId = aluno.Id,
            StartDate = inicio,
            Ativo = true,
            Observacao = dto.Observacao?.Trim(),
            CreatedById = UsuarioAtual()
        };

        db.StudentAllocations.Add(alocacao);
        await db.SaveChangesAsync();

        await db.Entry(alocacao).Reference(a => a.Student).LoadAsync();
        await db.Entry(alocacao).Reference(a => a.Location).LoadAsync();

        logger.LogInformation("Alocação criada: {Aluno} → {Unidade}.", aluno.FullName, unidade.Name);
        return Ok(Map(alocacao));
    }

    // ── Encerrar alocação ─────────────────────────────────────────────────────
    [HttpDelete("unidades-saude/{id}/estagiarios/{idEstagiario}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<AlocacaoDto>> Encerrar(
        Guid id, Guid idEstagiario, [FromBody] EncerrarAlocacaoDto? dto)
    {
        var alocacao = await db.StudentAllocations
            .Include(a => a.Student)
            .Include(a => a.Location)
            .FirstOrDefaultAsync(a => a.LocationId == id && a.StudentId == idEstagiario && a.Ativo);

        if (alocacao == null)
            return NotFound(new { message = "Alocação ativa não encontrada para este estagiário nesta unidade." });

        // Encerrar preserva a linha: é o histórico de onde o aluno esteve.
        alocacao.Ativo = false;
        alocacao.EndDate = dto?.DataFim ?? BrasiliaTime.Hoje;
        if (!string.IsNullOrWhiteSpace(dto?.Observacao))
            alocacao.Observacao = dto.Observacao.Trim();
        alocacao.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Alocação encerrada: {Aluno} deixou a unidade {Unidade}.",
            alocacao.Student.FullName, alocacao.Location.Name);

        return Ok(Map(alocacao));
    }

    // ── Tela geral de alocações ───────────────────────────────────────────────
    [HttpGet("alocacoes")]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<List<AlocacaoDto>>> GetTodas(
        [FromQuery] Guid? unidadeId,
        [FromQuery] Guid? estagiarioId,
        [FromQuery] bool? ativo,
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
        if (de.HasValue) query = query.Where(a => a.StartDate >= de.Value);
        if (ate.HasValue) query = query.Where(a => a.StartDate <= ate.Value);

        var alocacoes = await query
            .OrderByDescending(a => a.Ativo)
            .ThenByDescending(a => a.StartDate)
            .Take(500)
            .ToListAsync();

        return Ok(alocacoes.Select(Map));
    }

    /// <summary>
    /// Unidade de um estagiário. O aluno só enxerga a própria — e não a altera.
    /// </summary>
    [HttpGet("estagiarios/{id}/unidade")]
    public async Task<ActionResult<AlocacaoDto>> GetUnidadeDoEstagiario(Guid id)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;
        if (role == Roles.Aluno && UsuarioAtual() != id)
            return Forbid();

        var alocacao = await db.StudentAllocations
            .Include(a => a.Student)
            .Include(a => a.Location)
            .Include(a => a.CreatedBy)
            .FirstOrDefaultAsync(a => a.StudentId == id && a.Ativo);

        if (alocacao == null)
            return NotFound(new { message = "Nenhuma unidade alocada para este estagiário." });

        return Ok(Map(alocacao));
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
