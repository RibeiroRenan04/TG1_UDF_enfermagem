using EstagioCheck.API.Data;
using EstagioCheck.API.Services;
using EstagioCheck.API.Models;
using EstagioCheck.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Gestao)]
public class ReportsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ReportRowDto>>> Get()
    {
        var today = BrasiliaTime.Hoje;

        var members = await db.GroupMemberships
            .Include(m => m.Student)
            .Include(m => m.Group).ThenInclude(g => g.Schedules)
            .ToListAsync();

        var allRecs = await db.AttendanceRecords
            .Select(r => new { r.StudentId, r.Type, r.Status, r.RecordedAt })
            .ToListAsync();

        var rows = new List<ReportRowDto>();

        foreach (var member in members)
        {
            var sid = member.StudentId;
            var studentRecs = allRecs.Where(r => r.StudentId == sid).ToList();

            var required = member.Group.Schedules.Sum(s => s.RequiredHours);
            var approved = studentRecs.Count(r => r.Status == "aprovado");
            var irregular = studentRecs.Count(r => r.Status == "irregular");

            // Calcula horas
            var byDay = studentRecs
                .GroupBy(r => r.RecordedAt.Date)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        In = g.Where(r => r.Type == "check_in").Select(r => (DateTime?)r.RecordedAt).FirstOrDefault(),
                        Out = g.Where(r => r.Type == "check_out").Select(r => (DateTime?)r.RecordedAt).FirstOrDefault()
                    });

            double hours = 0;
            foreach (var pair in byDay.Values)
                if (pair.In.HasValue && pair.Out.HasValue)
                    hours += Math.Max(0, (pair.Out.Value - pair.In.Value).TotalHours);

            hours = Math.Round(hours, 1);

            // Pendências
            var checkInDates = studentRecs
                .Where(r => r.Type == "check_in")
                .Select(r => (ScheduleId: (Guid?)null, Date: DateOnly.FromDateTime(r.RecordedAt)))
                .ToHashSet();

            int pendencyDays = 0;
            double pendencyHours = 0;

            foreach (var sch in member.Group.Schedules)
            {
                var loc = await db.Locations.FindAsync(sch.LocationId);
                if (loc == null) continue;

                var current = sch.StartDate;
                var end = sch.EndDate < today ? sch.EndDate : today.AddDays(-1);
                while (current <= end)
                {
                    if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                    {
                        var hasCheckIn = await db.AttendanceRecords.AnyAsync(r =>
                            r.StudentId == sid && r.Type == "check_in" &&
                            r.ScheduleId == sch.Id &&
                            DateOnly.FromDateTime(r.RecordedAt) == current);

                        if (!hasCheckIn)
                        {
                            pendencyDays++;
                            if (TimeSpan.TryParse(loc.ShiftStart, out var s) &&
                                TimeSpan.TryParse(loc.ShiftEnd, out var e))
                                pendencyHours += (e - s).TotalHours;
                        }
                    }
                    current = current.AddDays(1);
                }
            }

            var pct = required > 0 ? Math.Min(100, hours / required * 100) : 0;

            rows.Add(new ReportRowDto
            {
                StudentId = sid,
                FullName = member.Student.FullName,
                Required = required,
                Hours = hours,
                Approved = approved,
                Irregular = irregular,
                PendencyDays = pendencyDays,
                PendencyHours = Math.Round(pendencyHours, 1),
                ProgressPercent = Math.Round(pct, 1),
                CertificateReleased = pct >= 100
            });
        }

        return Ok(rows.OrderBy(r => r.FullName));
    }
}
