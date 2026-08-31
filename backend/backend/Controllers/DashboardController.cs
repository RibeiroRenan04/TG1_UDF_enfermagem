using EstagioCheck.API.Data;
using EstagioCheck.API.Services;
using EstagioCheck.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "aluno";

        var query = db.AttendanceRecords.AsQueryable();
        if (role == "aluno")
            query = query.Where(r => r.StudentId == userId);

        var recs = await query
            .Select(r => new { r.Type, r.Status, r.RecordedAt, r.StudentId })
            .ToListAsync();

        var total = recs.Count;
        var approved = recs.Count(r => r.Status == "aprovado");
        var irregular = recs.Count(r => r.Status == "irregular");
        var pending = recs.Count(r => r.Status == "pendente");

        // Calcula horas (par check_in/check_out por aluno+dia)
        var byStudentDay = recs
            .GroupBy(r => $"{r.StudentId}|{r.RecordedAt:yyyy-MM-dd}")
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    In = g.Where(r => r.Type == "check_in").Select(r => (DateTime?)r.RecordedAt).FirstOrDefault(),
                    Out = g.Where(r => r.Type == "check_out").Select(r => (DateTime?)r.RecordedAt).FirstOrDefault()
                });

        double hours = 0;
        foreach (var pair in byStudentDay.Values)
            if (pair.In.HasValue && pair.Out.HasValue)
                hours += Math.Max(0, (pair.Out.Value - pair.In.Value).TotalHours);

        int required = 0;
        var pendencies = new List<PendencyDto>();

        if (role == "aluno")
        {
            var membership = await db.GroupMemberships
                .Include(m => m.Group).ThenInclude(g => g.Schedules)
                .FirstOrDefaultAsync(m => m.StudentId == userId);

            if (membership != null)
                required = membership.Group.Schedules.Sum(s => s.RequiredHours);

            pendencies = await GetStudentPendencies(userId);
        }

        return Ok(new DashboardStatsDto
        {
            Total = total,
            Approved = approved,
            Irregular = irregular,
            Pending = pending,
            Hours = Math.Round(hours, 1),
            Required = required,
            PendencyDays = pendencies.Count,
            PendencyHours = Math.Round(pendencies.Sum(p => p.ExpectedHours), 1),
            Pendencies = pendencies
        });
    }

    [HttpGet("pendencies")]
    public async Task<ActionResult<List<PendencyDto>>> GetPendencies([FromQuery] Guid? studentId)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "aluno";

        var targetId = (role == "aluno") ? userId : (studentId ?? userId);
        var result = await GetStudentPendencies(targetId);
        return Ok(result);
    }

    private async Task<List<PendencyDto>> GetStudentPendencies(Guid studentId)
    {
        var today = BrasiliaTime.Hoje;

        // Busca todos os cronogramas do grupo do aluno
        var membership = await db.GroupMemberships
            .FirstOrDefaultAsync(m => m.StudentId == studentId);
        if (membership == null) return [];

        var schedules = await db.RotationSchedules
            .Include(s => s.Location)
            .Where(s => s.GroupId == membership.GroupId)
            .ToListAsync();

        // Check-ins existentes
        var checkIns = await db.AttendanceRecords
            .Where(r => r.StudentId == studentId && r.Type == "check_in")
            .Select(r => new { r.ScheduleId, Date = DateOnly.FromDateTime(r.RecordedAt) })
            .ToListAsync();

        var checkInSet = checkIns
            .Select(r => $"{r.ScheduleId}|{r.Date:yyyy-MM-dd}")
            .ToHashSet();

        var pendencies = new List<PendencyDto>();
        foreach (var schedule in schedules)
        {
            var current = schedule.StartDate;
            var end = schedule.EndDate < today ? schedule.EndDate : today.AddDays(-1);
            while (current <= end)
            {
                // Apenas dias úteis (seg-sex)
                if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                {
                    var key = $"{schedule.Id}|{current:yyyy-MM-dd}";
                    if (!checkInSet.Contains(key))
                    {
                        // Horas esperadas com base no horário do local
                        double expectedHours = 8;
                        if (TimeSpan.TryParse(schedule.Location.ShiftStart, out var start) &&
                            TimeSpan.TryParse(schedule.Location.ShiftEnd, out var finish))
                            expectedHours = (finish - start).TotalHours;

                        pendencies.Add(new PendencyDto
                        {
                            PendencyDate = current,
                            ScheduleId = schedule.Id,
                            LocationName = schedule.Location.Name,
                            ExpectedHours = expectedHours
                        });
                    }
                }
                current = current.AddDays(1);
            }
        }

        return [.. pendencies.OrderByDescending(p => p.PendencyDate)];
    }
}
