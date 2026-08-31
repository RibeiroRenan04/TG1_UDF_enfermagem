using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.AcompanhamentoEGestao)]
public class EvaluationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EvaluationDto>>> GetAll()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var query = db.Evaluations
            .Include(e => e.Student)
            .Include(e => e.Preceptor)
            .AsQueryable();

        if (role == "preceptor")
            query = query.Where(e => e.PreceptorId == userId);

        var evals = await query.OrderByDescending(e => e.CreatedAt).ToListAsync();
        return Ok(evals.Select(Map));
    }

    [HttpPost]
    public async Task<ActionResult<EvaluationDto>> Create([FromBody] CreateEvaluationDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);

        var student = await db.Users.FindAsync(dto.StudentId);
        if (student == null) return NotFound(new { message = "Aluno não encontrado." });

        var eval = new Evaluation
        {
            StudentId = dto.StudentId,
            PreceptorId = userId,
            ScheduleId = dto.ScheduleId,
            ActivitiesScore = dto.ActivitiesScore,
            PostureScore = dto.PostureScore,
            PlanningScore = dto.PlanningScore,
            Comment = dto.Comment
        };

        db.Evaluations.Add(eval);
        await db.SaveChangesAsync();

        await db.Entry(eval).Reference(e => e.Student).LoadAsync();
        await db.Entry(eval).Reference(e => e.Preceptor).LoadAsync();
        return Ok(Map(eval));
    }

    private static EvaluationDto Map(Evaluation e) => new()
    {
        Id = e.Id, StudentId = e.StudentId, StudentName = e.Student?.FullName ?? string.Empty,
        PreceptorId = e.PreceptorId, PreceptorName = e.Preceptor?.FullName ?? string.Empty,
        ActivitiesScore = e.ActivitiesScore, PostureScore = e.PostureScore,
        PlanningScore = e.PlanningScore, Comment = e.Comment, CreatedAt = e.CreatedAt
    };
}
