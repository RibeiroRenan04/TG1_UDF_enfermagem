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
    /// <summary>
    /// Relatório de carga horária: uma linha por aluno, com o detalhe de cada turma.
    ///
    /// Antes havia uma linha por vínculo, e todas repetiam as horas e as pendências
    /// do aluno inteiro contra a carga de uma turma só — o PIC de 60 h aparecia
    /// "concluído" com as 191 h do estágio da manhã. Agora cada turma conta só os
    /// registros dos rodízios dela, e o certificado é decidido pelo total do aluno,
    /// com a mesma conta de horas aprovadas do certificado.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ReportRowDto>>> Get()
    {
        var members = await db.GroupMemberships
            .AsNoTracking()
            .Include(m => m.Student)
            .Include(m => m.Group).ThenInclude(g => g.Schedules)
            .ToListAsync();

        var alunoIds = members.Select(m => m.StudentId).Distinct().ToList();

        // De que turma é cada registro: pelo rodízio e, no ponto de atividade remota
        // sem rodízio, pela turma da atividade. Inclui rodízios de turmas das quais o
        // aluno já saiu — esses registros ficam fora das turmas atuais.
        var turmaDaEscala = await db.RotationSchedules.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.GroupId);
        var turmaDaAtividade = await db.RemoteActivities.AsNoTracking()
            .ToDictionaryAsync(a => a.Id, a => a.GroupId);

        var registrosPorAluno = (await db.AttendanceRecords.AsNoTracking()
                .Where(r => alunoIds.Contains(r.StudentId))
                .Select(r => new { r.StudentId, r.Type, r.Status, r.RecordedAt, r.ScheduleId, r.RemoteActivityId })
                .ToListAsync())
            .ToLookup(r => r.StudentId);

        // Pendências de todos os alunos de uma vez: calculadas aluno por aluno, o
        // relatório não terminava de carregar com a faculdade inteira.
        var pendenciasPorAluno = await pendenciasService.CalcularLoteAsync(alunoIds);

        var linhas = new List<ReportRowDto>();

        foreach (var vinculos in members.GroupBy(m => m.StudentId))
        {
            var aluno = vinculos.First().Student;
            var turmas = TurmasDoAluno.Ordenados(vinculos);
            var idsTurmas = turmas.Select(m => m.GroupId).ToHashSet();

            // Registro sem rodízio nem atividade é de quem não tinha programação:
            // com uma turma só, é dela; com várias, não há como saber.
            Guid? TurmaDe(Guid? scheduleId, Guid? atividadeId)
            {
                Guid? turma = scheduleId is Guid s && turmaDaEscala.TryGetValue(s, out var g1) ? g1
                            : atividadeId is Guid a && turmaDaAtividade.TryGetValue(a, out var g2) ? g2
                            : scheduleId == null && idsTurmas.Count == 1 ? idsTurmas.First()
                            : null;
                return turma is Guid t && idsTurmas.Contains(t) ? t : null;
            }

            var registros = registrosPorAluno[aluno.Id]
                .Select(r => (Turma: TurmaDe(r.ScheduleId, r.RemoteActivityId),
                              Hora: new CertificateService.RegistroHora(r.Type, r.Status, r.RecordedAt, r.ScheduleId)))
                .ToList();

            var pendencias = pendenciasPorAluno[aluno.Id]
                .Select(p => (Turma: TurmaDe(p.ScheduleId, null), Pendencia: p))
                .ToList();

            var detalhe = turmas.Select(m =>
            {
                var daTurma = registros.Where(r => r.Turma == m.GroupId).Select(r => r.Hora).ToList();
                var pendentes = pendencias.Where(p => p.Turma == m.GroupId).Select(p => p.Pendencia).ToList();
                var exigidas = m.Group.Schedules.Sum(s => s.RequiredHours);
                var horas = CertificateService.CalcularHorasAprovadas(daTurma);

                return new ReportTurmaDto
                {
                    GroupId = m.GroupId,
                    GroupCode = m.Group.Code,
                    GroupName = m.Group.Name,
                    Shift = TurmasDoAluno.Turno(m.Group),
                    Required = exigidas,
                    Hours = Math.Round(horas, 1),
                    Approved = daTurma.Count(r => r.Status == "aprovado"),
                    Irregular = daTurma.Count(r => r.Status == "irregular"),
                    PendencyDays = pendentes.Count,
                    PendencyHours = Math.Round(pendentes.Sum(p => p.ExpectedHours), 1),
                    ProgressPercent = Percentual(horas, exigidas)
                };
            }).ToList();

            // O total é a conta do certificado, sobre todos os registros do aluno.
            var total = CertificateService.CalcularHorasAprovadas(registros.Select(r => r.Hora));
            var exigidasTotal = detalhe.Sum(t => t.Required);
            var somaTurmas = registros
                .Where(r => r.Turma != null)
                .GroupBy(r => r.Turma)
                .Sum(g => CertificateService.CalcularHorasAprovadas(g.Select(r => r.Hora)));

            linhas.Add(new ReportRowDto
            {
                StudentId = aluno.Id,
                FullName = aluno.FullName,
                Rgm = aluno.Rgm,
                IsActive = aluno.IsActive,
                Required = exigidasTotal,
                Hours = Math.Round(total, 1),
                Approved = registros.Count(r => r.Hora.Status == "aprovado"),
                Irregular = registros.Count(r => r.Hora.Status == "irregular"),
                PendencyDays = pendencias.Count,
                PendencyHours = Math.Round(pendencias.Sum(p => p.Pendencia.ExpectedHours), 1),
                ProgressPercent = Percentual(total, exigidasTotal),
                CertificateReleased = exigidasTotal > 0 && total >= exigidasTotal,
                HoursOutsideGroups = Math.Round(Math.Max(0, total - somaTurmas), 1),
                Turmas = detalhe
            });
        }

        return Ok(linhas.OrderBy(l => l.FullName, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    private static double Percentual(double horas, int exigidas) =>
        exigidas > 0 ? Math.Round(Math.Min(100, horas / exigidas * 100), 1) : 0;
}
