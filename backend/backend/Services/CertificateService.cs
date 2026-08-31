using System.Security.Cryptography;
using System.Text;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>
/// Módulo de Certificação e Controle de Carga Horária (TGI 1.3.9).
/// Calcula as horas cumpridas (registros aprovados) frente à carga exigida e
/// determina a elegibilidade do aluno ao certificado. Cálculo sob demanda — sem
/// persistência — reutilizando a mesma lógica de pares check_in/check_out dos relatórios.
/// </summary>
public class CertificateService(AppDbContext db)
{
    /// <summary>Monta o certificado de um aluno. Retorna null se o id não for de um aluno.</summary>
    public async Task<CertificateDto?> ObterAsync(Guid studentId)
    {
        var student = await db.Users
            .FirstOrDefaultAsync(u => u.Id == studentId && u.Role == "aluno");
        if (student == null) return null;

        var membership = await db.GroupMemberships
            .Include(m => m.Group).ThenInclude(g => g.Schedules).ThenInclude(s => s.Location)
            .FirstOrDefaultAsync(m => m.StudentId == studentId);

        var schedules = membership?.Group.Schedules.ToList() ?? [];
        var required = schedules.Sum(s => s.RequiredHours);

        var recsRaw = await db.AttendanceRecords
            .Where(r => r.StudentId == studentId)
            .Select(r => new { r.Type, r.Status, r.RecordedAt })
            .ToListAsync();

        var recs = recsRaw.Select(r => new RegistroHora(r.Type, r.Status, r.RecordedAt));
        var completed = CalcularHorasAprovadas(recs);
        var pct = required > 0 ? Math.Min(100, completed / required * 100) : 0;
        var eligible = required > 0 && completed >= required;

        var locais = schedules
            .Select(s => s.Location.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        return new CertificateDto
        {
            StudentId = student.Id,
            StudentName = student.FullName,
            Rgm = student.Rgm,
            GroupName = membership?.Group.Name,
            CompletedHours = Math.Round(completed, 1),
            RequiredHours = required,
            ProgressPercent = Math.Round(pct, 1),
            Eligible = eligible,
            PeriodLabel = MontarPeriodo(schedules),
            Locations = locais,
            Institution = student.Institution,
            IssuedAt = BrasiliaTime.Agora,
            VerificationCode = GerarCodigo(student.Id, completed)
        };
    }

    /// <summary>Lista os certificados de todos os alunos vinculados a um grupo.</summary>
    public async Task<List<CertificateDto>> ListarAsync()
    {
        var studentIds = await db.GroupMemberships
            .Select(m => m.StudentId)
            .Distinct()
            .ToListAsync();

        var certificados = new List<CertificateDto>();
        foreach (var id in studentIds)
        {
            var cert = await ObterAsync(id);
            if (cert != null) certificados.Add(cert);
        }

        return certificados.OrderBy(c => c.StudentName).ToList();
    }

    /// <summary>Soma as horas de dias com par check_in/check_out, ambos aprovados.</summary>
    private static double CalcularHorasAprovadas(IEnumerable<RegistroHora> registros)
    {
        var porDia = registros
            .GroupBy(r => r.RecordedAt.Date)
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

    private static string? MontarPeriodo(List<Models.RotationSchedule> schedules)
    {
        if (schedules.Count == 0) return null;
        var inicio = schedules.Min(s => s.StartDate);
        var fim = schedules.Max(s => s.EndDate);
        return $"{inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}";
    }

    private static string GerarCodigo(Guid studentId, double horas)
    {
        var bruto = $"{studentId:N}|{horas:0.0}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(bruto));
        return Convert.ToHexString(hash)[..10];
    }

    private readonly record struct RegistroHora(string Type, string Status, DateTime RecordedAt);
}
