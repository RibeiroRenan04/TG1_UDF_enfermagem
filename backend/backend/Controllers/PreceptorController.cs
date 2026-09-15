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

        var result = new List<object>();
        foreach (var m in members)
        {
            var primeiraEscala = await db.RotationSchedules
                .Where(s => s.GroupId == m.GroupId)
                .OrderBy(s => s.StartDate)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync();

            // Mesma regra do painel do aluno: programação do dia + vigência do rodízio.
            var pendencias = await pendenciasService.CalcularAsync(m.StudentId);

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
