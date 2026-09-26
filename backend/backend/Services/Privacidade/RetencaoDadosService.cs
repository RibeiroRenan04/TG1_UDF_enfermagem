using EstagioCheck.API.Data;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services.Privacidade;

/// <summary>
/// Retenção (LGPD art. 15/16): uma vez por dia apaga o que já cumpriu a finalidade — códigos de
/// recuperação de senha vencidos e registros de auditoria além do prazo configurado.
/// </summary>
public class RetencaoDadosService(IServiceScopeFactory scopes, IConfiguration config, ILogger<RetencaoDadosService> logger)
    : BackgroundService
{
    /// <summary>
    /// 5 anos por padrão. O Marco Civil (art. 15) exige ao menos 6 meses de registro de acesso;
    /// o prazo maior cobre a guarda de documentos acadêmicos do estágio.
    /// </summary>
    public const int RetencaoAuditoriaDiasPadrao = 1825;

    private const int Lote = 5000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Espera a aplicação subir antes da primeira rodada.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var dias = config.GetValue("Auditoria:RetencaoDias", RetencaoAuditoriaDiasPadrao);
                var (codigos, logs) = await ExpurgarAsync(db, BrasiliaTime.Agora, dias, stoppingToken);

                if (codigos + logs > 0)
                    logger.LogInformation(
                        "Retenção: {Codigos} código(s) de senha vencido(s) e {Logs} registro(s) de auditoria expurgados.",
                        codigos, logs);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha na rotina de retenção de dados.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public static async Task<(int Codigos, int Logs)> ExpurgarAsync(
        AppDbContext db, DateTime agora, int retencaoAuditoriaDias, CancellationToken ct = default)
    {
        // Um dia de folga: o código já expirou, mas ainda serve para investigar um pedido recente.
        var limiteCodigos = agora.AddDays(-1);
        var codigos = await db.PasswordResetCodes.Where(c => c.ExpiresAt < limiteCodigos).ToListAsync(ct);
        db.PasswordResetCodes.RemoveRange(codigos);

        var limiteLogs = agora.AddDays(-retencaoAuditoriaDias);
        var totalLogs = 0;
        while (true)
        {
            var logs = await db.AuditLogs.Where(l => l.OccurredAt < limiteLogs).OrderBy(l => l.Id).Take(Lote).ToListAsync(ct);
            if (logs.Count == 0) break;
            db.AuditLogs.RemoveRange(logs);
            await db.SaveChangesAsync(ct);
            totalLogs += logs.Count;
            if (logs.Count < Lote) break;
        }

        await db.SaveChangesAsync(ct);
        return (codigos.Count, totalLogs);
    }
}
