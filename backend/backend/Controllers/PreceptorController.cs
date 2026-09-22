using EstagioCheck.API.Data;
using EstagioCheck.API.Services;
using EstagioCheck.API.Models;
using EstagioCheck.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

/// <summary>Visão específica para preceptores: alunos e presenças irregulares.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.AcompanhamentoEGestao)]
public class PreceptorController(AppDbContext db, PendenciasService pendenciasService) : ControllerBase
{
    [HttpGet("students")]
    public async Task<ActionResult> GetStudents()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        // Grupos onde sou preceptor
        var scheduleQuery = db.RotationSchedules.AsQueryable();
        if (role == "preceptor")
            scheduleQuery = scheduleQuery.Where(s => s.PreceptorId == userId);

        var groupIds = await scheduleQuery.Select(s => s.GroupId).Distinct().ToListAsync();

        var members = await db.GroupMemberships
            .Include(m => m.Student)
            .Include(m => m.Group)
            .Where(m => groupIds.Contains(m.GroupId))
            .ToListAsync();

        // Primeiro rodízio de cada turma e pendências de todos os alunos de uma vez:
        // aluno por aluno, a turma inteira levava minutos para abrir.
        var primeiraEscalaPorTurma = (await db.RotationSchedules
                .Where(s => groupIds.Contains(s.GroupId))
                .Select(s => new { s.GroupId, s.Id, s.StartDate })
                .ToListAsync())
            .GroupBy(s => s.GroupId)
            .ToDictionary(g => g.Key, g => (Guid?)g.OrderBy(s => s.StartDate).First().Id);

        // Mesma regra do painel do aluno: programação do dia + vigência do rodízio.
        var pendenciasPorAluno = await pendenciasService.CalcularLoteAsync(
            [.. members.Select(m => m.StudentId)]);

        var result = new List<object>();
        foreach (var m in members)
        {
            var primeiraEscala = primeiraEscalaPorTurma.GetValueOrDefault(m.GroupId);
            var pendencias = pendenciasPorAluno[m.StudentId];

            result.Add(new
            {
                id = m.StudentId,
                fullName = m.Student.FullName,
                email = m.Student.Email,
                groupCode = m.Group.Code,
                groupName = m.Group.Name,
                pendencyDays = pendencias.Count,
                pendencyHours = Math.Round(pendencias.Sum(p => p.ExpectedHours), 1),
                scheduleId = primeiraEscala
            });
        }

        return Ok(result);
    }

    [HttpGet("irregular-records")]
    public async Task<ActionResult> GetIrregularRecords()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var query = db.AttendanceRecords
            .Include(r => r.Student)
            .Include(r => r.Schedule)
            .Where(r => r.Status == "irregular");

        if (role == "preceptor")
        {
            var scheduleIds = await db.RotationSchedules
                .Where(s => s.PreceptorId == userId)
                .Select(s => s.Id)
                .ToListAsync();
            query = query.Where(r => r.ScheduleId != null && scheduleIds.Contains(r.ScheduleId!.Value));
        }

        var recs = await query.OrderByDescending(r => r.RecordedAt).ToListAsync();

        return Ok(recs.Select(r => new
        {
            id = r.Id,
            type = r.Type,
            recordedAt = r.RecordedAt,
            status = r.Status,
            irregularityReason = r.IrregularityReason,
            distanceMeters = r.DistanceMeters,
            photoUrl = r.PhotoUrl,
            studentId = r.StudentId,
            studentName = r.Student?.FullName,
            scheduleId = r.ScheduleId
        }));
    }
}
