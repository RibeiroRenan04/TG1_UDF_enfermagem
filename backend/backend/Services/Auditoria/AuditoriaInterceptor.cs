using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace EstagioCheck.API.Services.Auditoria;

/// <summary>
/// Registra toda criação, alteração e exclusão feita pelo EF na mesma transação do dado: não existe
/// gravação sem trilha, nem trilha de gravação que falhou. Alteração guarda só os campos que mudaram.
/// </summary>
public class AuditoriaInterceptor(IHttpContextAccessor http) : SaveChangesInterceptor
{
    /// <summary>Cache e códigos de senha não são dado de negócio; a própria trilha não se audita.</summary>
    private static readonly HashSet<Type> Ignoradas =
        [typeof(AuditLog), typeof(GeocodingCacheEntry), typeof(PasswordResetCode)];

    /// <summary>
    /// Minimização (LGPD art. 6º, III): a trilha registra que o campo mudou, não o valor. Assim ela não
    /// vira uma cópia dos dados pessoais que sobreviveria à anonimização do titular.
    /// </summary>
    private static readonly HashSet<string> Mascarados =
    [
        $"{nameof(ApplicationUser)}.{nameof(ApplicationUser.PasswordHash)}",
        $"{nameof(ApplicationUser)}.{nameof(ApplicationUser.FullName)}",
        $"{nameof(ApplicationUser)}.{nameof(ApplicationUser.Email)}",
        $"{nameof(ApplicationUser)}.{nameof(ApplicationUser.Phone)}",
        $"{nameof(ApplicationUser)}.{nameof(ApplicationUser.Rgm)}",
        $"{nameof(AttendanceRecord)}.{nameof(AttendanceRecord.Latitude)}",
        $"{nameof(AttendanceRecord)}.{nameof(AttendanceRecord.Longitude)}",
        $"{nameof(AttendanceRecord)}.{nameof(AttendanceRecord.PhotoUrl)}",
        $"{nameof(FormativeFollowup)}.{nameof(FormativeFollowup.PreceptorSignedIp)}",
        $"{nameof(FormativeFollowup)}.{nameof(FormativeFollowup.StudentSignedIp)}",
        $"{nameof(FormativeFollowup)}.{nameof(FormativeFollowup.PreceptorSignedName)}",
        $"{nameof(FormativeFollowup)}.{nameof(FormativeFollowup.StudentSignedName)}",
    ];

    public const string ValorMascarado = "[protegido]";
    private const int TamanhoMaximoValor = 500;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Registrar(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Registrar(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Registrar(DbContext? db)
    {
        if (db == null) return;

        var origem = ContextoRequisicao.De(http.HttpContext);
        var logs = new List<AuditLog>();

        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (Ignoradas.Contains(entry.Metadata.ClrType)) continue;

            var detalhes = Detalhar(entry);
            // Alteração que não mudou valor nenhum (ex.: Update com os mesmos dados) não é evento.
            if (entry.State == EntityState.Modified && detalhes.Count == 0) continue;

            logs.Add(new AuditLog
            {
                UserId = origem.UsuarioId,
                UserRole = origem.Papel,
                Action = entry.State switch
                {
                    EntityState.Added => AcoesAuditoria.Criacao,
                    EntityState.Modified => AcoesAuditoria.Alteracao,
                    _ => AcoesAuditoria.Exclusao
                },
                Entity = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name,
                EntityId = Chave(entry),
                Details = JsonSerializer.Serialize(detalhes),
                IpAddress = origem.Ip,
                UserAgent = origem.UserAgent,
                CorrelationId = origem.IdCorrelacao
            });
        }

        if (logs.Count > 0) db.Set<AuditLog>().AddRange(logs);
    }

    private static string? Chave(EntityEntry entry)
    {
        var chave = entry.Metadata.FindPrimaryKey();
        if (chave == null) return null;

        var partes = chave.Properties.Select(p => entry.Property(p.Name)).ToList();
        // Chave gerada pelo banco ainda não existe antes do INSERT.
        if (partes.Any(p => p.IsTemporary)) return null;

        return string.Join(",", partes.Select(p => Convert.ToString(p.CurrentValue ?? p.OriginalValue)));
    }

    private static Dictionary<string, object?> Detalhar(EntityEntry entry)
    {
        var tipo = entry.Metadata.ClrType.Name;
        var campos = new Dictionary<string, object?>();

        foreach (var prop in entry.Properties)
        {
            var nome = prop.Metadata.GetColumnName();
            var mascarado = Mascarados.Contains($"{tipo}.{prop.Metadata.Name}");

            switch (entry.State)
            {
                case EntityState.Added when prop.CurrentValue != null:
                    campos[nome] = mascarado ? ValorMascarado : Valor(prop.CurrentValue);
                    break;

                case EntityState.Modified when prop.IsModified && !Equals(prop.OriginalValue, prop.CurrentValue):
                    campos[nome] = mascarado
                        ? new { de = ValorMascarado, para = ValorMascarado }
                        : new { de = Valor(prop.OriginalValue), para = Valor(prop.CurrentValue) };
                    break;

                case EntityState.Deleted when prop.OriginalValue != null:
                    campos[nome] = mascarado ? ValorMascarado : Valor(prop.OriginalValue);
                    break;
            }
        }

        return campos;
    }

    private static object? Valor(object? valor) => valor is string s && s.Length > TamanhoMaximoValor
        ? s[..TamanhoMaximoValor] + "…"
        : valor;
}
