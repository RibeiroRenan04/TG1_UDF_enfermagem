using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Controllers;

/// <summary>
/// Gestão de usuários. A leitura é liberada para o professor e para a coordenadora
/// (perfil de consulta e acompanhamento); toda escrita é exclusiva do professor.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Gestao)]
public class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll()
    {
        var users = await db.Users
            .Include(u => u.GroupMembership).ThenInclude(m => m!.Group)
            .OrderBy(u => u.FullName)
            .ToListAsync();

        return Ok(users.Select(MapToDto));
    }

    // ── Painel de alunos com filtros ──────────────────────────────────────────
    [HttpGet("students")]
    public async Task<ActionResult<List<UserDto>>> GetStudents(
        [FromQuery] int? semester,
        [FromQuery] string? shift,
        [FromQuery] bool? isActive)
    {
        var query = db.Users
            .Include(u => u.GroupMembership).ThenInclude(m => m!.Group)
            .Where(u => u.Role == "aluno");

        if (semester.HasValue)
            query = query.Where(u => u.Semester == semester.Value);

        if (!string.IsNullOrEmpty(shift))
            query = query.Where(u => u.Shift == shift.ToLower());

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        var users = await query.OrderBy(u => u.FullName).ToListAsync();
        return Ok(users.Select(MapToDto));
    }

    // ── Painel de preceptores ─────────────────────────────────────────────────
    [HttpGet("preceptors")]
    public async Task<ActionResult<List<UserDto>>> GetPreceptors()
    {
        var users = await db.Users
            .Where(u => u.Role != Roles.Aluno)
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .ToListAsync();

        return Ok(users.Select(MapToDto));
    }

    // ── Toggle ativo/inativo ──────────────────────────────────────────────────
    [HttpPatch("{id}/active")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> ToggleActive(Guid id)
    {
        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(new { id = user.Id, isActive = user.IsActive });
    }

    [HttpPatch("{id}/assign-group")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> AssignGroup(Guid id, [FromBody] AssignGroupDto dto)
    {
        var user = await db.Users
            .Include(u => u.GroupMembership)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();
        if (user.Role != "aluno")
            return BadRequest(new { message = "Apenas alunos podem ser atribuídos a grupos." });

        if (user.GroupMembership != null)
            db.GroupMemberships.Remove(user.GroupMembership);

        if (dto.GroupId.HasValue)
        {
            var group = await db.StudentGroups.FindAsync(dto.GroupId.Value);
            if (group == null) return NotFound(new { message = "Grupo não encontrado." });

            db.GroupMemberships.Add(new GroupMembership
            {
                StudentId = id,
                GroupId = dto.GroupId.Value
            });
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── Permissão de atraso ───────────────────────────────────────────────────
    /// <summary>
    /// Autoriza (ou revoga) a chegada do aluno após o horário previsto de início do
    /// estágio. A carga horária do dia continua sendo exigida — a permissão apenas
    /// impede que o registro tardio seja tratado como irregularidade de horário.
    /// </summary>
    [HttpPatch("{id}/late-permission")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UserDto>> SetLatePermission(Guid id, [FromBody] LatePermissionDto dto)
    {
        var user = await db.Users
            .Include(u => u.GroupMembership).ThenInclude(m => m!.Group)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();
        if (user.Role != Roles.Aluno)
            return BadRequest(new { message = "A permissão de atraso se aplica apenas a alunos." });

        user.AllowLateArrival = dto.AllowLateArrival;
        user.LateArrivalNote = dto.AllowLateArrival && !string.IsNullOrWhiteSpace(dto.Note)
            ? dto.Note.Trim()
            : null;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(MapToDto(user));
    }

    // ── Troca de turno do aluno ───────────────────────────────────────────────
    /// <summary>
    /// Altera o turno do aluno — usado quando há troca autorizada entre alunos
    /// (por exemplo, da manhã para a tarde). Somente o turno é editável aqui.
    /// </summary>
    [HttpPatch("{id}/shift")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UserDto>> UpdateShift(Guid id, [FromBody] UpdateShiftDto dto)
    {
        var shift = dto.Shift?.Trim().ToLowerInvariant();
        if (shift is not ("manha" or "tarde" or "noite"))
            return BadRequest(new { message = "Turno deve ser 'manha', 'tarde' ou 'noite'." });

        var user = await db.Users
            .Include(u => u.GroupMembership).ThenInclude(m => m!.Group)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();
        if (user.Role != Roles.Aluno)
            return BadRequest(new { message = "Apenas alunos possuem turno de estágio." });

        user.Shift = shift;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(MapToDto(user));
    }

    // ── Criar preceptor / professor / coordenadora ────────────────────────────
    /// <summary>
    /// Cadastra preceptor, professor (supervisor) ou coordenadora. O e-mail
    /// institucional (@cs.udf.edu.br) não é obrigatório: muitos preceptores são
    /// profissionais externos à UDF e utilizam e-mail próprio, que também serve
    /// como login.
    /// </summary>
    [HttpPost("staff")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UserDto>> CreateStaff([FromBody] CreateStaffDto dto)
    {
        if (dto.Role is not (Roles.Preceptor or Roles.Supervisor or Roles.Coordenadora))
            return BadRequest(new { message = "Papel deve ser 'preceptor', 'supervisor' ou 'coordenadora'." });

        var email = dto.Email.Trim().ToLower();

        var exists = await db.Users.AnyAsync(u => u.Email == email);
        if (exists)
            return Conflict(new { message = "E-mail já cadastrado." });

        var user = new ApplicationUser
        {
            FullName = dto.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role = dto.Role,
            Institution = dto.Institution?.Trim(),
            Phone = dto.Phone?.Trim(),
            MustChangePassword = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), MapToDto(user));
    }

    // ── Importação em lote de alunos ──────────────────────────────────────────
    /// <summary>
    /// Importa alunos a partir da planilha acadêmica. O login de cada aluno é o
    /// e-mail institucional gerado aqui e a senha inicial é o próprio RGM, trocada
    /// no primeiro acesso. Os logins gerados voltam na resposta: sem eles o
    /// supervisor não tem como informar ao aluno com que e-mail entrar.
    /// </summary>
    [HttpPost("bulk-import")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<BulkImportResponseDto>> BulkImport([FromBody] BulkImportRequestDto dto)
    {
        int imported = 0, updated = 0;
        var errors = new List<string>();
        var emailsUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var logins = new List<ImportedStudentLoginDto>();

        foreach (var s in dto.Students)
        {
            try
            {
                var rgm = NormalizarRgm(s.Rgm);
                if (string.IsNullOrEmpty(rgm))
                {
                    errors.Add($"RGM \"{s.Rgm}\": valor inválido.");
                    continue;
                }

                // Reconhece também o formato antigo (com o "14" na frente) para não
                // duplicar alunos já cadastrados antes da mudança de formato.
                var rgmLegado = $"{PrefixoRgmLegado}{rgm}";
                var existing = await db.Users
                    .FirstOrDefaultAsync(u => u.Rgm == rgm || u.Rgm == rgmLegado);
                if (existing != null)
                {
                    existing.Rgm = rgm;
                    existing.Semester = s.Semester;
                    existing.Shift = s.Shift.ToLower();
                    // Aluno que ainda não fez o primeiro acesso tem a senha inicial
                    // igual ao RGM: reemite o hash no formato novo.
                    if (existing.MustChangePassword)
                        existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(rgm);
                    // Preenche o e-mail institucional se ainda não houver.
                    if (string.IsNullOrEmpty(existing.Email))
                    {
                        var emailGerado = await GerarEmailInstitucionalAsync(s.FullName, emailsUsados);
                        existing.Email = emailGerado;
                        existing.MustSetEmail = false;
                        logins.Add(new ImportedStudentLoginDto(existing.FullName, rgm, emailGerado));
                    }
                    existing.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }
                else
                {
                    // E-mail institucional gerado automaticamente: nome.ultimonome@cs.udf.edu.br
                    var email = await GerarEmailInstitucionalAsync(s.FullName, emailsUsados);
                    var fullName = s.FullName.Trim();

                    db.Users.Add(new ApplicationUser
                    {
                        FullName = fullName,
                        Email = email,
                        // Senha inicial igual ao RGM; MustChangePassword força a troca.
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(rgm),
                        Role = Roles.Aluno,
                        Rgm = rgm, // o RGM é a matrícula do aluno
                        Semester = s.Semester,
                        Shift = s.Shift.ToLower(),
                        MustChangePassword = true,
                        MustSetEmail = false
                    });

                    logins.Add(new ImportedStudentLoginDto(fullName, rgm, email));
                    imported++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"RGM {s.Rgm}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync();
        return Ok(new BulkImportResponseDto(imported, updated, errors, logins));
    }

    // ── Normalização do RGM ───────────────────────────────────────────────────
    /// <summary>Prefixo que os RGMs da UDF traziam e que deixou de ser usado.</summary>
    private const string PrefixoRgmLegado = "14";

    /// <summary>
    /// Padroniza o RGM: mantém apenas dígitos e remove o "14" do início, que não faz
    /// mais parte do formato. RGMs que são só o próprio prefixo são descartados.
    /// </summary>
    internal static string NormalizarRgm(string? rgm)
    {
        var digitos = new string((rgm ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitos.StartsWith(PrefixoRgmLegado, StringComparison.Ordinal)
            && digitos.Length > PrefixoRgmLegado.Length)
            digitos = digitos[PrefixoRgmLegado.Length..];
        return digitos;
    }

    // ── Geração de e-mail institucional ───────────────────────────────────────
    private const string DominioInstitucional = "@cs.udf.edu.br";

    /// <summary>
    /// Gera um e-mail institucional no formato "primeironome.ultimonome@cs.udf.edu.br",
    /// garantindo unicidade (no lote e no banco) com sufixo numérico em caso de colisão.
    /// </summary>
    private async Task<string> GerarEmailInstitucionalAsync(string fullName, HashSet<string> emailsUsados)
    {
        var prefixo = MontarPrefixoEmail(fullName);
        var candidato = $"{prefixo}{DominioInstitucional}";
        var n = 1;
        while (emailsUsados.Contains(candidato) || await db.Users.AnyAsync(u => u.Email == candidato))
        {
            n++;
            candidato = $"{prefixo}{n}{DominioInstitucional}";
        }
        emailsUsados.Add(candidato);
        return candidato;
    }

    /// <summary>"João da Silva Santos" → "joao.santos" (sem acentos, minúsculo).</summary>
    private static string MontarPrefixoEmail(string fullName)
    {
        var partes = RemoverAcentos(fullName)
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray()))
            .Where(p => p.Length > 0)
            .ToList();

        if (partes.Count == 0) return "aluno";
        if (partes.Count == 1) return partes[0];
        return $"{partes[0]}.{partes[^1]}";
    }

    private static string RemoverAcentos(string texto)
    {
        var decomposto = texto.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposto.Length);
        foreach (var ch in decomposto)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    // ── Avançar semestre ──────────────────────────────────────────────────────
    [HttpPost("advance-semester")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<AdvanceSemesterResponseDto>> AdvanceSemester()
    {
        // 1. Alunos do 8° semestre → formados
        var semester8 = await db.Users
            .Where(u => u.Role == "aluno" && u.Semester == 8 && u.IsActive)
            .ToListAsync();

        var totalHoursMap = await BuildTotalHoursMap(semester8.Select(u => u.Id).ToList());

        foreach (var student in semester8)
        {
            db.StudentSemesterHistories.Add(new StudentSemesterHistory
            {
                StudentId = student.Id,
                Semester = 8,
                TotalHours = totalHoursMap.GetValueOrDefault(student.Id, 0)
            });
            student.IsActive = false;
            student.UpdatedAt = DateTime.UtcNow;
        }

        // 2. Alunos do 7° semestre → avançam para o 8°
        var semester7 = await db.Users
            .Where(u => u.Role == "aluno" && u.Semester == 7 && u.IsActive)
            .ToListAsync();

        var totalHoursMap7 = await BuildTotalHoursMap(semester7.Select(u => u.Id).ToList());

        foreach (var student in semester7)
        {
            db.StudentSemesterHistories.Add(new StudentSemesterHistory
            {
                StudentId = student.Id,
                Semester = 7,
                TotalHours = totalHoursMap7.GetValueOrDefault(student.Id, 0)
            });
            student.Semester = 8;
            student.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        return Ok(new AdvanceSemesterResponseDto(semester7.Count, semester8.Count));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private async Task<Dictionary<Guid, decimal>> BuildTotalHoursMap(List<Guid> studentIds)
    {
        if (studentIds.Count == 0) return [];

        // Conta check-ins aprovados; cada check-in representa um turno de 4h
        return await db.AttendanceRecords
            .Where(a => studentIds.Contains(a.StudentId) && a.Status == "aprovado" && a.Type == "check_in")
            .GroupBy(a => a.StudentId)
            .Select(g => new { StudentId = g.Key, TotalHours = (decimal)g.Count() * 4 })
            .ToDictionaryAsync(x => x.StudentId, x => x.TotalHours);
    }

    private static UserDto MapToDto(ApplicationUser u) => new()
    {
        Id = u.Id,
        FullName = u.FullName,
        Email = u.Email,
        Rgm = u.Rgm,
        Semester = u.Semester,
        Shift = u.Shift,
        Role = u.Role,
        IsActive = u.IsActive,
        AllowLateArrival = u.AllowLateArrival,
        LateArrivalNote = u.LateArrivalNote,
        MustChangePassword = u.MustChangePassword,
        MustSetEmail = u.MustSetEmail,
        TermsAcceptedAt = u.TermsAcceptedAt,
        GroupId = u.GroupMembership?.GroupId,
        GroupCode = u.GroupMembership?.Group?.Code,
        GroupName = u.GroupMembership?.Group?.Name
    };
}
