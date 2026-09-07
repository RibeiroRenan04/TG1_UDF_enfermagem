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
/// Programação diária do aluno: onde ele deveria estar e o que deveria fazer.
///
/// A tela de ponto consulta este endpoint antes de qualquer coisa — é ele que diz
/// se o dia pede localização, se pede o código de uma atividade remota ou se não
/// há ponto a registrar.
/// </summary>
[ApiController]
[Route("api/programacao")]
[Authorize]
public class ProgramacaoController(AppDbContext db, ProgramacaoService programacao) : ControllerBase
{
    /// <summary>Programação de um dia. Sem data, hoje.</summary>
    [HttpGet]
    public async Task<ActionResult<ProgramacaoDiaDto>> Get(
        [FromQuery] DateOnly? data,
        [FromQuery] string? turno,
        [FromQuery] Guid? studentId)
    {
        var alvo = await ResolverAlunoAsync(studentId);
        if (alvo is not Guid aluno) return Forbid();

        var dia = await programacao.ObterAsync(aluno, data ?? BrasiliaTime.Hoje, turno);
        return Ok(Map(dia, await ParticipacoesAsync(aluno, [dia])));
    }

    /// <summary>
    /// Programação de um intervalo — o calendário do aluno, já com feriados,
    /// dias remotos e trocas de local aplicados.
    /// </summary>
    [HttpGet("periodo")]
    public async Task<ActionResult<List<ProgramacaoDiaDto>>> GetPeriodo(
        [FromQuery] DateOnly de,
        [FromQuery] DateOnly ate,
        [FromQuery] string? turno,
        [FromQuery] Guid? studentId)
    {
        if (ate < de)
            return BadRequest(new { message = "A data final não pode ser anterior à inicial." });

        // Um intervalo grande vira uma consulta por dia: 92 dias cobrem um semestre
        // inteiro em partes e mantêm a resposta barata.
        if (de.AddDays(92) < ate)
            return BadRequest(new { message = "Consulte no máximo 92 dias por vez." });

        var alvo = await ResolverAlunoAsync(studentId);
        if (alvo is not Guid aluno) return Forbid();

        var dias = await programacao.ObterIntervaloAsync(aluno, de, ate, turno);
        var participacoes = await ParticipacoesAsync(aluno, dias);

        return Ok(dias.Select(dia => Map(dia, participacoes)));
    }

    /// <summary>
    /// Participações do aluno nas atividades que aparecem no período, em uma
    /// consulta só — o intervalo pode cobrir um semestre inteiro.
    /// </summary>
    private async Task<Dictionary<Guid, RemoteActivityParticipation>> ParticipacoesAsync(
        Guid studentId, IEnumerable<ProgramacaoService.ProgramacaoDia> dias)
    {
        var ids = dias.SelectMany(d => d.AtividadesRemotas).Select(a => a.Id).Distinct().ToList();
        if (ids.Count == 0) return [];

        return await db.RemoteActivityParticipations
            .AsNoTracking()
            .Where(p => p.StudentId == studentId && ids.Contains(p.RemoteActivityId))
            .ToDictionaryAsync(p => p.RemoteActivityId);
    }

    /// <summary>
    /// Aluno consultado. O aluno só enxerga a própria programação; os demais perfis
    /// precisam informar de quem é.
    /// </summary>
    private async Task<Guid?> ResolverAlunoAsync(Guid? studentId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Aluno;

        if (role == Roles.Aluno) return userId;
        if (!studentId.HasValue) return userId;

        return await db.Users.AnyAsync(u => u.Id == studentId.Value) ? studentId.Value : null;
    }

    private static ProgramacaoDiaDto Map(
        ProgramacaoService.ProgramacaoDia dia,
        Dictionary<Guid, RemoteActivityParticipation> participacoes)
    {
        var atividades = dia.AtividadesRemotas
            .Select(a => AtividadesRemotasController.MapParaAluno(a, participacoes.GetValueOrDefault(a.Id)))
            .ToList();

        return new ProgramacaoDiaDto
        {
            Data = dia.Data,
            Turno = dia.Turno,
            TurnoLabel = Turnos.Rotulo(dia.Turno),
            Modo = dia.Modo,
            ModoLabel = ModoAtividade.Rotulo(dia.Modo),
            Validacao = dia.Validacao,
            ExigePonto = dia.ExigePonto,
            ScheduleId = dia.ScheduleId,
            PeriodLabel = dia.PeriodLabel,
            ActivityType = dia.ActivityType,
            Location = dia.Local == null ? null : new LocationDto
            {
                Id = dia.Local.Id,
                Name = dia.Local.Name,
                Address = dia.Local.Address,
                Latitude = dia.Local.Latitude,
                Longitude = dia.Local.Longitude,
                RadiusMeters = dia.Local.RadiusMeters,
                IsInstitution = dia.Local.IsInstitution,
                ShiftStart = dia.Local.ShiftStart,
                ShiftEnd = dia.Local.ShiftEnd,
                CodigoCnes = dia.Local.CodigoCnes
            },
            Motivo = dia.Motivo,
            ExcecaoId = dia.ExcecaoId,
            TipoExcecao = dia.TipoExcecao,
            TipoExcecaoLabel = dia.TipoExcecao == null ? null : TipoExcecao.Rotulo(dia.TipoExcecao),
            AbrangenciaExcecao = dia.AbrangenciaExcecao,
            AtividadesRemotas = atividades
        };
    }
}
