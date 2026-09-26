using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using EstagioCheck.API.Services.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Gestao)]
public class UsersController(AppDbContext db, ConflitoTurmasService conflitos, ProtecaoAcessoService protecao) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll()
    {
        var users = await db.Users.AsNoTracking()
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group)
            .OrderBy(u => u.FullName)
            .ToListAsync();

        return Ok(users.Select(MapToDto));
    }

    [HttpGet("students")]
    public async Task<ActionResult<List<UserDto>>> GetStudents(
        [FromQuery] int? semester,
        [FromQuery] string? shift,
        [FromQuery] bool? isActive)
    {
        var query = db.Users.AsNoTracking()
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group)
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

    [HttpGet("preceptors")]
    public async Task<ActionResult<List<UserDto>>> GetPreceptors()
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Role != Roles.Aluno)
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .ToListAsync();

        return Ok(users.Select(MapToDto));
    }

    [HttpPatch("{id}/active")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> ToggleActive(Guid id)
    {
        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.IsActive = !user.IsActive;
        user.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();
        return Ok(new { id = user.Id, isActive = user.IsActive });
    }

    /// <summary>
    /// Aluno volta para o RGM; equipe (preceptor, professor, secretaria) recebe uma senha provisória,
    /// devolvida só nesta resposta para o professor repassar. Nos dois casos a troca é exigida no
    /// próximo acesso e o bloqueio por tentativas é liberado. É o caminho de recuperação enquanto o
    /// envio de e-mail está desativado.
    /// </summary>
    [HttpPost("{id}/reset-password")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> ResetarSenha(Guid id)
    {
        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString())
            return BadRequest(ErrosApi.Corpo(
                "Você não pode redefinir a própria senha por aqui. Peça a outro professor.", code: "redefinir_propria_senha"));

        if (string.IsNullOrEmpty(user.Email))
            return BadRequest(ErrosApi.Corpo("O usuário não tem e-mail de login cadastrado."));

        string? senhaProvisoria = null;
        string message;

        if (user.Role == Roles.Aluno)
        {
            var rgm = NormalizarRgm(user.Rgm);
            if (string.IsNullOrEmpty(rgm))
                return BadRequest(ErrosApi.Corpo("O aluno não tem RGM cadastrado."));

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(rgm);
            message = $"Senha de {user.FullName} redefinida para o RGM.";
        }
        else
        {
            senhaProvisoria = SenhaProvisoria.Gerar();
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(senhaProvisoria);
            message = $"Senha provisória de {user.FullName} gerada. Repasse-a pessoalmente: ela não será exibida de novo.";
        }

        user.MustChangePassword = true;
        user.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();
        // Quem pediu ajuda normalmente acabou de errar a senha várias vezes.
        protecao.Limpar($"login:{user.Email.ToLower()}");

        return Ok(new { message, senhaProvisoria });
    }

    /// <summary>
    /// <c>GroupIds</c> é a lista completa: o que não vier é desvinculado (<c>GroupId</c>, legado,
    /// vale como lista de uma turma). O aluno pode estar em várias turmas; só a agenda
    /// impossível é recusada.
    /// </summary>
    [HttpPatch("{id}/assign-group")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> AssignGroup(Guid id, [FromBody] AssignGroupDto dto)
    {
        var user = await db.Users
            .Include(u => u.GroupMemberships)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();
        if (user.Role != "aluno")
            return BadRequest(new { message = "Apenas alunos podem ser atribuídos a grupos." });

        var desejadas = (dto.GroupIds ?? (dto.GroupId.HasValue ? [dto.GroupId.Value] : []))
            .Distinct()
            .ToList();

        var existentes = await db.StudentGroups
            .Where(g => desejadas.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync();

        if (existentes.Count != desejadas.Count)
            return NotFound(new { message = "Grupo não encontrado." });

        foreach (var vinculo in user.GroupMemberships.Where(m => !desejadas.Contains(m.GroupId)).ToList())
            db.GroupMemberships.Remove(vinculo);

        var jaVinculadas = user.GroupMemberships.Select(m => m.GroupId).ToHashSet();

        foreach (var groupId in desejadas.Where(g => !jaVinculadas.Contains(g)))
        {
            var conflito = await conflitos.VerificarAsync(id, groupId);
            if (conflito != null)
                return Conflict(ErrosApi.Corpo(conflito, "groupId", "conflito_agenda"));

            db.GroupMemberships.Add(new GroupMembership { StudentId = id, GroupId = groupId });
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>A carga horária do dia continua exigida; a permissão só evita a irregularidade de horário.</summary>
    [HttpPatch("{id}/late-permission")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UserDto>> SetLatePermission(Guid id, [FromBody] LatePermissionDto dto)
    {
        var user = await db.Users
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();
        if (user.Role != Roles.Aluno)
            return BadRequest(new { message = "A permissão de atraso se aplica apenas a alunos." });

        user.AllowLateArrival = dto.AllowLateArrival;
        user.LateArrivalNote = dto.AllowLateArrival && !string.IsNullOrWhiteSpace(dto.Note)
            ? dto.Note.Trim()
            : null;
        user.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();
        return Ok(MapToDto(user));
    }

    [HttpPatch("{id}/shift")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UserDto>> UpdateShift(Guid id, [FromBody] UpdateShiftDto dto)
    {
        var shift = dto.Shift?.Trim().ToLowerInvariant();
        if (shift is not ("manha" or "tarde" or "noite"))
            return BadRequest(new { message = "Turno deve ser 'manha', 'tarde' ou 'noite'." });

        var user = await db.Users
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();
        if (user.Role != Roles.Aluno)
            return BadRequest(new { message = "Apenas alunos possuem turno de estágio." });

        user.Shift = shift;
        user.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();
        return Ok(MapToDto(user));
    }

    /// <summary>Vínculo é lista fechada: em texto livre a mesma unidade chegava grafada de várias formas.</summary>
    [HttpGet("vinculos-institucionais")]
    public async Task<ActionResult<List<string>>> GetVinculosInstitucionais() =>
        Ok(await VinculosInstitucionaisAsync());

    private async Task<List<string>> VinculosInstitucionaisAsync() =>
        await db.Locations
            .Where(l => l.Ativo)
            .Select(l => l.Name)
            .Distinct()
            .OrderBy(nome => nome)
            .ToListAsync();

    /// <summary>E-mail institucional não é obrigatório: muitos preceptores são externos à UDF.</summary>
    [HttpPost("staff")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UserDto>> CreateStaff([FromBody] CreateStaffDto dto)
    {
        if (dto.Role is not (Roles.Preceptor or Roles.Supervisor or Roles.Secretaria))
            return BadRequest(new { message = "Papel deve ser 'preceptor', 'supervisor' ou 'secretaria'." });

        var vinculo = dto.Institution?.Trim();
        if (!string.IsNullOrEmpty(vinculo))
        {
            var opcoes = await VinculosInstitucionaisAsync();
            var escolhido = opcoes.FirstOrDefault(o => string.Equals(o, vinculo, StringComparison.OrdinalIgnoreCase));
            if (escolhido == null)
                return BadRequest(new
                {
                    message = $"Vínculo institucional \"{vinculo}\" não está cadastrado. "
                            + "Escolha uma das unidades da lista ou cadastre a unidade antes.",
                    code = "vinculo_invalido"
                });

            vinculo = escolhido;
        }

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
            Institution = string.IsNullOrEmpty(vinculo) ? null : vinculo,
            Phone = dto.Phone?.Trim(),
            MustChangePassword = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), MapToDto(user));
    }

    /// <summary>Login = e-mail institucional gerado; senha inicial = RGM. Os logins voltam na resposta para o professor repassar.</summary>
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

                // Aceita também o formato antigo (com "14") para não duplicar alunos.
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
                    if (string.IsNullOrEmpty(existing.Email))
                    {
                        var emailGerado = await GerarEmailInstitucionalAsync(s.FullName, emailsUsados);
                        existing.Email = emailGerado;
                        existing.MustSetEmail = false;
                        logins.Add(new ImportedStudentLoginDto(existing.FullName, rgm, emailGerado));
                    }
                    existing.UpdatedAt = BrasiliaTime.Agora;
                    updated++;
                }
                else
                {
                    var email = await GerarEmailInstitucionalAsync(s.FullName, emailsUsados);
                    var fullName = s.FullName.Trim();

                    db.Users.Add(new ApplicationUser
                    {
                        FullName = fullName,
                        Email = email,
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

    private const string PrefixoRgmLegado = "14";

    internal static string NormalizarRgm(string? rgm)
    {
        var digitos = new string((rgm ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitos.StartsWith(PrefixoRgmLegado, StringComparison.Ordinal)
            && digitos.Length > PrefixoRgmLegado.Length)
            digitos = digitos[PrefixoRgmLegado.Length..];
        return digitos;
    }

    private const string DominioInstitucional = "@cs.udf.edu.br";

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
            student.UpdatedAt = BrasiliaTime.Agora;
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
            student.UpdatedAt = BrasiliaTime.Agora;
        }

        await db.SaveChangesAsync();

        return Ok(new AdvanceSemesterResponseDto(semester7.Count, semester8.Count));
    }

    /// <summary>Nada é apagado; a carga horária do momento vai para o histórico. Desfaz-se em <see cref="Reativar"/>.</summary>
    [HttpPost("{id}/concluir")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UserDto>> Concluir(Guid id)
    {
        var user = await db.Users
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound(new { message = "Aluno não encontrado." });
        if (user.Role != Roles.Aluno)
            return BadRequest(new { message = "Apenas alunos podem ser marcados como concluintes." });
        if (!user.IsActive)
            return Conflict(new
            {
                message = $"{user.FullName} já está entre os alunos inativos.",
                code = "aluno_ja_inativo"
            });

        var horas = await BuildTotalHoursMap([user.Id]);
        db.StudentSemesterHistories.Add(new StudentSemesterHistory
        {
            StudentId = user.Id,
            // Aluno sem semestre cadastrado conclui pelo último semestre do estágio.
            Semester = user.Semester ?? 8,
            TotalHours = horas.GetValueOrDefault(user.Id, 0)
        });

        user.IsActive = false;
        user.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();
        return Ok(MapToDto(user));
    }

    [HttpPost("{id}/reativar")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UserDto>> Reativar(Guid id)
    {
        var user = await db.Users
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound(new { message = "Aluno não encontrado." });
        if (user.Role != Roles.Aluno)
            return BadRequest(new { message = "Apenas alunos podem ser reativados por esta ação." });
        if (user.IsActive)
            return Conflict(new
            {
                message = $"{user.FullName} já está entre os alunos ativos.",
                code = "aluno_ja_ativo"
            });

        user.IsActive = true;
        user.UpdatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();
        return Ok(MapToDto(user));
    }

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
        // Campos singulares = turma principal (telas antigas); Groups = lista completa.
        GroupId = TurmasDoAluno.Principal(u.GroupMemberships)?.GroupId,
        GroupCode = TurmasDoAluno.Principal(u.GroupMemberships)?.Group?.Code,
        GroupName = TurmasDoAluno.Principal(u.GroupMemberships)?.Group?.Name,
        Groups = [.. TurmasDoAluno.Ordenados(u.GroupMemberships)
            .Where(m => m.Group != null)
            .Select(m => new UserGroupDto
            {
                Id = m.GroupId,
                Code = m.Group.Code,
                Name = m.Group.Name
            })]
    };
}
