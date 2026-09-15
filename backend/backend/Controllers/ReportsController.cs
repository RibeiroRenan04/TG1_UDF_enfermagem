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
public class ReportsController(AppDbContext db, PendenciasService pendenciasService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ReportRowDto>>> Get()
    {
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

            // Pendências: mesma regra do painel do aluno — programação do dia (sábado
            // programado conta, feriado não) dentro da vigência do rodízio e da turma.
            var pendencias = await pendenciasService.CalcularAsync(sid);
            var pendencyDays = pendencias.Count;
            var pendencyHours = pendencias.Sum(p => p.ExpectedHours);

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
