using System.Security.Cryptography;
using System.Text;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>Horas aprovadas frente à carga exigida e elegibilidade ao certificado, calculadas sob demanda.</summary>
public class CertificateService(AppDbContext db)
{
    /// <summary><c>null</c> se o id não for de um aluno.</summary>
    public async Task<CertificateDto?> ObterAsync(Guid studentId) =>
        (await MontarAsync([studentId])).FirstOrDefault();

    /// <summary>Certificados de todos os alunos vinculados a alguma turma.</summary>
    public async Task<List<CertificateDto>> ListarAsync()
    {
        var studentIds = await db.GroupMemberships.AsNoTracking()
            .Select(m => m.StudentId)
            .Distinct()
            .ToListAsync();

        return await MontarAsync(studentIds);
    }

    /// <summary>
    /// Monta os certificados de vários alunos com quatro consultas no total, qualquer que seja o
    /// número de alunos. Antes a lista chamava <see cref="ObterAsync"/> aluno por aluno (três
    /// consultas cada) e a tela de certificados não terminava de carregar.
    /// </summary>
    private async Task<List<CertificateDto>> MontarAsync(List<Guid> studentIds)
    {
        if (studentIds.Count == 0) return [];

        var alunos = await db.Users.AsNoTracking()
            .Where(u => studentIds.Contains(u.Id) && u.Role == Roles.Aluno)
            .Select(u => new { u.Id, u.FullName, u.Rgm, u.Institution })
            .ToListAsync();

        var vinculos = await db.GroupMemberships.AsNoTracking()
            .Where(m => studentIds.Contains(m.StudentId))
            .Select(m => new { m.StudentId, m.GroupId, m.CreatedAt, m.Group.Code, m.Group.Name })
            .ToListAsync();

        var grupoIds = vinculos.Select(v => v.GroupId).Distinct().ToList();
        var rodiziosPorGrupo = (await db.RotationSchedules.AsNoTracking()
                .Where(s => grupoIds.Contains(s.GroupId))
                .Select(s => new { s.GroupId, s.RequiredHours, s.StartDate, s.EndDate, Local = s.Location.Name })
                .ToListAsync())
            .ToLookup(s => s.GroupId);

        // Só o par aprovado conta hora; filtrar no banco evita trazer o ponto inteiro da faculdade.
        var registrosPorAluno = (await db.AttendanceRecords.AsNoTracking()
                .Where(r => studentIds.Contains(r.StudentId) && r.Status == "aprovado")
                .Select(r => new { r.StudentId, r.Type, r.Status, r.RecordedAt, r.ScheduleId })
                .ToListAsync())
            .ToLookup(r => r.StudentId, r => new RegistroHora(r.Type, r.Status, r.RecordedAt, r.ScheduleId));

        var vinculosPorAluno = vinculos.ToLookup(v => v.StudentId);
        var emissao = BrasiliaTime.Agora;

        return [.. alunos.Select(aluno =>
        {
            var meus = vinculosPorAluno[aluno.Id].ToList();
            var rodizios = meus.SelectMany(v => rodiziosPorGrupo[v.GroupId]).ToList();
            var exigidas = rodizios.Sum(s => s.RequiredHours);
            var registros = registrosPorAluno[aluno.Id].ToList();
            var cumpridas = CalcularHorasAprovadas(registros);

            DateTime? ultimoDia = registros.Count == 0 ? null : registros.Max(r => r.RecordedAt).Date;

            var turmas = TurmasDoAluno.Ordenados(meus.Select(v => new GroupMembership
            {
                GroupId = v.GroupId,
                CreatedAt = v.CreatedAt,
                Group = new StudentGroup { Id = v.GroupId, Code = v.Code, Name = v.Name }
            }));

            return new CertificateDto
            {
                StudentId = aluno.Id,
                StudentName = aluno.FullName,
                Rgm = aluno.Rgm,
                GroupName = turmas.Count == 0 ? null : string.Join(", ", turmas.Select(m => m.Group.Name)),
                CompletedHours = Math.Round(cumpridas, 1),
                RequiredHours = exigidas,
                ProgressPercent = Math.Round(exigidas > 0 ? Math.Min(100, cumpridas / exigidas * 100) : 0, 1),
                Eligible = exigidas > 0 && cumpridas >= exigidas,
                PeriodLabel = rodizios.Count == 0
                    ? null
                    : $"{rodizios.Min(s => s.StartDate):dd/MM/yyyy} a {ultimoDia ?? rodizios.Max(s => s.EndDate).ToDateTime(TimeOnly.MinValue):dd/MM/yyyy}",
                Locations = [.. rodizios.Select(s => s.Local).Distinct(StringComparer.OrdinalIgnoreCase).Order()],
                Institution = aluno.Institution,
                IssuedAt = emissao,
                VerificationCode = GerarCodigo(aluno.Id, cumpridas)
            };
        }).OrderBy(c => c.StudentName)];
    }

    /// <summary>
    /// Pares check_in/check_out aprovados, formados por dia e por rodízio: só por dia, quem cursa
    /// duas turmas tinha a entrada da manhã casada com a saída da tarde.
    /// </summary>
    public static double CalcularHorasAprovadas(IEnumerable<RegistroHora> registros)
    {
        var porDia = registros
            .GroupBy(r => (r.RecordedAt.Date, r.ScheduleId))
            .Select(g => new
            {
                In = g.Where(r => r.Type == "check_in" && r.Status == "aprovado")
                      .Select(r => (DateTime?)r.RecordedAt).FirstOrDefault(),
                Out = g.Where(r => r.Type == "check_out" && r.Status == "aprovado")
                       .Select(r => (DateTime?)r.RecordedAt).FirstOrDefault()
            });

        double horas = 0;
        foreach (var dia in porDia)
            if (dia.In.HasValue && dia.Out.HasValue)
                horas += Math.Max(0, (dia.Out.Value - dia.In.Value).TotalHours);

        return horas;
    }

    private static string GerarCodigo(Guid studentId, double horas)
    {
        var bruto = $"{studentId:N}|{horas:0.0}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(bruto));
        return Convert.ToHexString(hash)[..10];
    }

    public readonly record struct RegistroHora(string Type, string Status, DateTime RecordedAt, Guid? ScheduleId = null);
}
