using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace EstagioCheck.API.Services.Privacidade;

/// <summary>Direitos do titular (LGPD art. 18): acesso/portabilidade e anonimização.</summary>
public class PrivacidadeService(AppDbContext db)
{
    public const string PrefixoNomeAnonimizado = "Titular anonimizado";

    /// <summary>Todos os dados pessoais do titular em formato estruturado (JSON), ou <c>null</c> se não existe.</summary>
    public async Task<object?> ExportarAsync(Guid userId)
    {
        var u = await db.Users.AsNoTracking()
            .Include(x => x.GroupMemberships).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(x => x.Id == userId);
        if (u == null) return null;

        var presencas = await db.AttendanceRecords.AsNoTracking()
            .Where(a => a.StudentId == userId)
            .OrderBy(a => a.RecordedAt)
            .Select(a => new
            {
                a.Id, tipo = a.Type, registradoEm = a.RecordedAt, local = a.Location != null ? a.Location.Name : null,
                a.Latitude, a.Longitude, distanciaMetros = a.DistanceMeters, atividades = a.ActivitiesDescription,
                a.Status, motivoIrregularidade = a.IrregularityReason, validadoEm = a.ValidatedAt
            })
            .ToListAsync();

        var avaliacoes = await db.Evaluations.AsNoTracking()
            .Where(e => e.StudentId == userId)
            .Select(e => new
            {
                e.Id, criadaEm = e.CreatedAt, preceptor = e.Preceptor.FullName,
                notaAtividades = e.ActivitiesScore, notaPostura = e.PostureScore, notaPlanejamento = e.PlanningScore,
                comentario = e.Comment
            })
            .ToListAsync();

        var acompanhamentos = await db.FormativeFollowups.AsNoTracking()
            .Where(f => f.StudentId == userId || f.PreceptorId == userId)
            .Select(f => new
            {
                f.Id, papel = f.StudentId == userId ? "aluno" : "preceptor", f.Status,
                inicio = f.FollowUpStart, fim = f.FollowUpEnd, f.Potencialidades, f.AspectosAprimorar,
                f.SituacoesRelevantes, f.EvolucaoSemanal, f.ObservacoesDocente,
                assinadoPeloPreceptorEm = f.PreceptorSignedAt, assinadoPeloAlunoEm = f.StudentSignedAt,
                ipAssinatura = f.StudentId == userId ? f.StudentSignedIp : f.PreceptorSignedIp
            })
            .ToListAsync();

        var irregularidades = await db.PointIrregularities.AsNoTracking()
            .Where(i => i.StudentId == userId)
            .Select(i => new
            {
                i.Id, tipo = i.Type, ocorridaEm = i.OccurredOn, descricao = i.Description, i.Status,
                notaPreceptor = i.PreceptorNote, notaProfessor = i.ProfessorNote
            })
            .ToListAsync();

        var alocacoes = await db.StudentAllocations.AsNoTracking()
            .Where(a => a.StudentId == userId)
            .Select(a => new { a.Id, unidade = a.Location.Name, turno = a.Shift, inicio = a.StartDate, fim = a.EndDate, a.Ativo })
            .ToListAsync();

        var atividadesRemotas = await db.RemoteActivityParticipations.AsNoTracking()
            .Where(p => p.StudentId == userId)
            .Select(p => new { p.Id, atividade = p.RemoteActivity.Title, registradaEm = p.RegisteredAt, resposta = p.TaskResponse })
            .ToListAsync();

        var historico = await db.StudentSemesterHistories.AsNoTracking()
            .Where(h => h.StudentId == userId)
            .Select(h => new { semestre = h.Semester, totalHoras = h.TotalHours, registradoEm = h.RecordedAt })
            .ToListAsync();

        // Só metadados: o campo Detalhes pode citar dados de outras pessoas.
        var acessos = await db.AuditLogs.AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.OccurredAt)
            .Take(500)
            .Select(l => new { ocorridoEm = l.OccurredAt, acao = l.Action, area = l.Entity, sucesso = l.Success, ip = l.IpAddress })
            .ToListAsync();

        return new
        {
            geradoEm = BrasiliaTime.Agora,
            finalidade = "Atendimento ao direito de acesso e portabilidade do titular (LGPD, art. 18, II e V).",
            cadastro = new
            {
                u.Id, nomeCompleto = u.FullName, u.Email, u.Rgm, perfil = u.Role, semestre = u.Semester,
                turno = u.Shift, telefone = u.Phone, vinculoInstitucional = u.Institution, ativo = u.IsActive,
                termoAceitoEm = u.TermsAcceptedAt, criadoEm = u.CreatedAt, atualizadoEm = u.UpdatedAt,
                turmas = u.GroupMemberships.Where(m => m.Group != null).Select(m => new { m.Group.Code, m.Group.Name })
            },
            registrosDePresenca = presencas,
            avaliacoes,
            acompanhamentosFormativos = acompanhamentos,
            irregularidades,
            alocacoes,
            atividadesRemotas,
            historicoSemestral = historico,
            historicoDeAcessos = acessos
        };
    }

    /// <summary>
    /// Anonimiza o titular (LGPD art. 18, IV e art. 16): remove o que o identifica, mas preserva o
    /// registro acadêmico (horas, avaliações, irregularidades), cuja guarda é obrigação da instituição.
    /// Só vale para conta inativa — aluno em curso ainda precisa dos dados. Não salva: quem chama
    /// grava junto com o registro de auditoria.
    /// </summary>
    public async Task<(StatusAnonimizacao Status, string? Erro)> AnonimizarAsync(Guid userId, Guid solicitanteId)
    {
        if (userId == solicitanteId)
            return (StatusAnonimizacao.Recusada, "Não é possível anonimizar a própria conta.");

        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (u == null) return (StatusAnonimizacao.NaoEncontrado, "Usuário não encontrado.");
        if (u.IsActive)
            return (StatusAnonimizacao.Recusada, "Desative a conta antes de anonimizar: o titular ainda está em atividade.");
        if (u.FullName.StartsWith(PrefixoNomeAnonimizado))
            return (StatusAnonimizacao.Recusada, "Este titular já foi anonimizado.");

        var nomeAnonimo = $"{PrefixoNomeAnonimizado} {u.Id.ToString("N")[..8]}";
        var emailAntigo = u.Email;

        u.FullName = nomeAnonimo;
        u.Email = null;
        u.Rgm = null;
        u.Phone = null;
        u.Institution = null;
        u.LateArrivalNote = null;
        // Senha aleatória descartada: a conta não pode mais ser usada.
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        u.MustChangePassword = true;
        u.UpdatedAt = BrasiliaTime.Agora;

        // Localização e foto do ponto identificam a rotina da pessoa; hora e local do estágio ficam.
        foreach (var p in await db.AttendanceRecords.Where(a => a.StudentId == userId).ToListAsync())
        {
            p.Latitude = 0;
            p.Longitude = 0;
            p.PhotoUrl = null;
        }

        foreach (var f in await db.FormativeFollowups
                     .Where(f => f.StudentSignedUserId == userId || f.PreceptorSignedUserId == userId)
                     .ToListAsync())
        {
            if (f.StudentSignedUserId == userId) { f.StudentSignedName = nomeAnonimo; f.StudentSignedIp = null; }
            if (f.PreceptorSignedUserId == userId) { f.PreceptorSignedName = nomeAnonimo; f.PreceptorSignedIp = null; }
        }

        if (!string.IsNullOrEmpty(emailAntigo))
            db.PasswordResetCodes.RemoveRange(await db.PasswordResetCodes.Where(c => c.Email == emailAntigo).ToListAsync());

        return (StatusAnonimizacao.Concluida, null);
    }
}

public enum StatusAnonimizacao { Concluida, NaoEncontrado, Recusada }
