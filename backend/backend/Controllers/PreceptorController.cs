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
public class PreceptorController(AppDbContext db) : ControllerBase
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

        var today = BrasiliaTime.Hoje;

        var result = new List<object>();
        foreach (var m in members)
        {
            // Pendências simplificadas
            var schedules = await db.RotationSchedules
                .Include(s => s.Location)
                .Where(s => s.GroupId == m.GroupId)
                .ToListAsync();

            int pendencyDays = 0;
            double pendencyHours = 0;

            foreach (var sch in schedules)
            {
                var current = sch.StartDate;
                var end = sch.EndDate < today ? sch.EndDate : today.AddDays(-1);
                while (current <= end)
                {
                    if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                    {
                        var hasCheckIn = await db.AttendanceRecords.AnyAsync(r =>
                            r.StudentId == m.StudentId && r.Type == "check_in" &&
                            r.ScheduleId == sch.Id &&
                            DateOnly.FromDateTime(r.RecordedAt) == current);

                        if (!hasCheckIn)
                        {
                            pendencyDays++;
                            if (TimeSpan.TryParse(sch.Location.ShiftStart, out var s) &&
                                TimeSpan.TryParse(sch.Location.ShiftEnd, out var e))
                                pendencyHours += (e - s).TotalHours;
                        }
                    }
                    current = current.AddDays(1);
                }
            }

            result.Add(new
            {
                id = m.StudentId,
                fullName = m.Student.FullName,
                email = m.Student.Email,
                groupCode = m.Group.Code,
                groupName = m.Group.Name,
                pendencyDays,
                pendencyHours = Math.Round(pendencyHours, 1),
                scheduleId = schedules.FirstOrDefault()?.Id
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
