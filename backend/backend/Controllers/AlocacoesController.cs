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
/// Alocação por turno: manhã numa unidade e tarde em outra, nunca duas ativas no mesmo
/// turno (API + índice único). Trocar de unidade encerra a alocação e cria outra.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class AlocacoesController(
    AppDbContext db, ILogger<AlocacoesController> logger, EscopoPreceptorService escopoPreceptor) : ControllerBase
{
    /// <summary>O aluno consulta a unidade, mas só enxerga a própria alocação.</summary>
    [HttpGet("unidades-saude/{id}/estagiarios")]
    public async Task<ActionResult<List<AlocacaoDto>>> GetEstagiariosDaUnidade(
        Guid id, [FromQuery] bool incluirEncerradas = false)
    {
        if (!await db.Locations.AnyAsync(l => l.Id == id))
            return NotFound(new { message = "Unidade não encontrada." });

        var query = db.StudentAllocations.AsNoTracking()
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
        else if (User.IsInRole(Roles.Preceptor))
        {
            var escopo = await escopoPreceptor.CarregarAsync(UsuarioAtual());
            if (!escopo.Locais.Contains(id)) return Forbid();
            query = query.Where(a => escopo.Alunos.Contains(a.StudentId));
        }

        var alocacoes = await query
            .OrderByDescending(a => a.Ativo)
            .ThenBy(a => a.Student.FullName)
            .ThenBy(a => a.Shift)
            .ToListAsync();

        return Ok(alocacoes.Select(Map));
    }

    [HttpGet("unidades-saude/{id}/estagiarios-disponiveis")]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<List<EstagiarioDisponivelDto>>> GetDisponiveis(
        Guid id, [FromQuery] string? busca)
    {
        var query = db.Users.AsNoTracking()
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group)
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
        var ativas = await db.StudentAllocations.AsNoTracking()
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
                // Cursando dois rodízios, o aluno aparece com as duas turmas ("T01, T02").
                Turma = TurmasDoAluno.Codigos(u.GroupMemberships),
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

            // A anterior termina no dia em que a nova começa: o fim não pode vir antes do início.
            if (inicio < alocacaoDoTurno.StartDate)
                return BadRequest(new
                {
                    message = $"A nova alocação começa em {inicio:dd/MM/yyyy}, antes do início da alocação atual "
                            + $"em \"{alocacaoDoTurno.Location.Name}\" ({alocacaoDoTurno.StartDate:dd/MM/yyyy}). "
                            + $"Informe uma data de início a partir de {alocacaoDoTurno.StartDate:dd/MM/yyyy}.",
                    code = "inicio_anterior_alocacao_atual",
                    turno,
                    inicioAlocacaoAtual = alocacaoDoTurno.StartDate
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

    /// <summary>Com vários turnos, <paramref name="turno"/> diz qual encerrar; sem ele, só se houver uma.</summary>
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

        if (alocacao == null)
            return BadRequest(new
            {
                message = "O estagiário tem mais de uma alocação ativa nesta unidade. "
                        + "Informe o turno que deve ser encerrado.",
                code = "turno_obrigatorio",
                turnos = ativas.Select(a => a.Shift).ToList()
            });

        var fim = dto?.DataFim ?? BrasiliaTime.Hoje;
        if (fim < alocacao.StartDate)
            return BadRequest(new
            {
                message = $"A data de término ({fim:dd/MM/yyyy}) é anterior ao início da alocação "
                        + $"({alocacao.StartDate:dd/MM/yyyy}). Informe uma data a partir de "
                        + $"{alocacao.StartDate:dd/MM/yyyy}.",
                code = "fim_anterior_inicio",
                inicioAlocacao = alocacao.StartDate
            });

        alocacao.Ativo = false;
        alocacao.EndDate = fim;
        if (!string.IsNullOrWhiteSpace(dto?.Observacao))
            alocacao.Observacao = dto.Observacao.Trim();
        alocacao.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Alocação encerrada: {Aluno} deixou a unidade {Unidade} ({Turno}).",
            alocacao.Student.FullName, alocacao.Location.Name, alocacao.Shift);

        return Ok(Map(alocacao));
    }

    [HttpGet("alocacoes")]
    [Authorize(Roles = Roles.Gestao)]
    /// <summary>A busca é feita aqui: filtrar só a página carregada escondia quem estava nas outras.</summary>
    public async Task<ActionResult<AlocacoesPaginaDto>> GetTodas(
        [FromQuery] Guid? unidadeId,
        [FromQuery] Guid? estagiarioId,
        [FromQuery] bool? ativo,
        [FromQuery] string? turno,
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        [FromQuery] string? busca,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanhoPagina = 50,
        [FromQuery] string? ordenarPor = null,
        [FromQuery] string? direcao = null)
    {
        pagina = Math.Max(1, pagina);
        tamanhoPagina = Math.Clamp(tamanhoPagina, 10, 200);

        var query = db.StudentAllocations.AsQueryable();

        if (unidadeId.HasValue) query = query.Where(a => a.LocationId == unidadeId.Value);
        if (estagiarioId.HasValue) query = query.Where(a => a.StudentId == estagiarioId.Value);
        if (ativo.HasValue) query = query.Where(a => a.Ativo == ativo.Value);
        var turnoFiltro = Turnos.Normalizar(turno);
        if (turnoFiltro != null) query = query.Where(a => a.Shift == turnoFiltro);
        if (de.HasValue) query = query.Where(a => a.StartDate >= de.Value);
        if (ate.HasValue) query = query.Where(a => a.StartDate <= ate.Value);

        var termo = busca?.Trim().ToLower();
        if (!string.IsNullOrEmpty(termo))
            query = query.Where(a => a.Student.FullName.ToLower().Contains(termo)
                                  || (a.Student.Rgm != null && a.Student.Rgm.Contains(termo)));

        var total = await query.CountAsync();
        var ativas = await query.CountAsync(a => a.Ativo);

        var alocacoes = await Ordenar(query, ordenarPor, direcao)
            .Include(a => a.Student)
            .Include(a => a.Location)
            .Include(a => a.CreatedBy)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync();

        return Ok(new AlocacoesPaginaDto
        {
            Itens = [.. alocacoes.Select(Map)],
            Total = total,
            Ativas = ativas,
            Pagina = pagina,
            TamanhoPagina = tamanhoPagina
        });
    }

    /// <summary>
    /// Ordenação escolhida no cabeçalho da tabela. A lista é paginada aqui, então a
    /// ordem precisa sair do banco: ordenar só a página na tela misturaria as demais.
    /// Sem coluna reconhecida, vale a ordem padrão (ativas e mais recentes primeiro).
    /// O desempate por aluno e Id mantém a paginação estável.
    /// </summary>
    private static IQueryable<StudentAllocation> Ordenar(
        IQueryable<StudentAllocation> query, string? ordenarPor, string? direcao)
    {
        var desc = string.Equals(direcao, "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<StudentAllocation> Por<T>(System.Linq.Expressions.Expression<Func<StudentAllocation, T>> chave)
            => desc ? query.OrderByDescending(chave) : query.OrderBy(chave);

        IOrderedQueryable<StudentAllocation>? ordenada = ordenarPor?.ToLowerInvariant() switch
        {
            "estagiario" => Por(a => a.Student.FullName),
            "rgm" => Por(a => a.Student.Rgm),
            "unidade" => Por(a => a.Location.Name),
            // Turno na ordem do dia, não alfabética (manhã, tarde, noite).
            "turno" => Por(a => a.Shift == Turnos.Manha ? 0 : a.Shift == Turnos.Tarde ? 1 : 2),
            "inicio" => Por(a => a.StartDate),
            "fim" => Por(a => a.EndDate),
            "situacao" => Por(a => a.Ativo),
            _ => null
        };

        if (ordenada == null)
            return query
                .OrderByDescending(a => a.Ativo)
                .ThenByDescending(a => a.StartDate)
                .ThenBy(a => a.Shift)
                .ThenBy(a => a.Student.FullName)
                .ThenBy(a => a.Id);

        return ordenada.ThenBy(a => a.Student.FullName).ThenBy(a => a.Id);
    }

    /// <summary>O aluno só enxerga a própria. Sem <paramref name="turno"/>, vale o turno cadastrado.</summary>
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
