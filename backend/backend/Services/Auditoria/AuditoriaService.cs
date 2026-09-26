using EstagioCheck.API.Data;
using EstagioCheck.API.Models;
using System.Text.Json;

namespace EstagioCheck.API.Services.Auditoria;

/// <summary>
/// Eventos que não são gravação de dado (login, exportação LGPD…). Alterações de dados já são
/// registradas sozinhas pelo <see cref="AuditoriaInterceptor"/>.
/// </summary>
public class AuditoriaService(AppDbContext db, IHttpContextAccessor http)
{
    /// <summary>Entra no próximo SaveChanges — na mesma transação da ação auditada.</summary>
    public void Registrar(string acao, string entidade, string? entidadeId = null, object? detalhes = null,
        bool sucesso = true, Guid? usuarioId = null, string? papel = null)
    {
        var origem = ContextoRequisicao.De(http.HttpContext);

        db.AuditLogs.Add(new AuditLog
        {
            // Login ainda não tem usuário no token: quem chama informa de quem é o evento.
            UserId = usuarioId ?? origem.UsuarioId,
            UserRole = papel ?? origem.Papel,
            Action = acao,
            Entity = entidade,
            EntityId = entidadeId,
            Details = detalhes == null ? null : JsonSerializer.Serialize(detalhes),
            Success = sucesso,
            IpAddress = origem.Ip,
            UserAgent = origem.UserAgent,
            CorrelationId = origem.IdCorrelacao
        });
    }

    /// <summary>Para eventos sem outra gravação junto (ex.: login recusado).</summary>
    public async Task RegistrarAsync(string acao, string entidade, string? entidadeId = null, object? detalhes = null,
        bool sucesso = true, Guid? usuarioId = null, string? papel = null)
    {
        Registrar(acao, entidade, entidadeId, detalhes, sucesso, usuarioId, papel);
        await db.SaveChangesAsync();
    }
}
