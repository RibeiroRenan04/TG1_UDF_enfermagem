using EstagioCheck.API.Data;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services.Geocoding;

/// <summary>
/// Consome a fila de geocodificação fora do ciclo das requisições HTTP.
///
/// Processa uma unidade por vez — o ritmo real é imposto pelo
/// <see cref="NominatimGeocodingService"/>, que respeita o intervalo mínimo entre
/// requisições. Uma falha em uma unidade nunca derruba o serviço: a unidade fica
/// com status "erro" e o administrador reprocessa quando quiser.
/// </summary>
public class GeocodingBackgroundService(
    GeocodingQueue fila,
    IServiceScopeFactory scopeFactory,
    ILogger<GeocodingBackgroundService> logger) : BackgroundService
{
    /// <summary>Lotes concluídos são esquecidos depois disso.</summary>
    private static readonly TimeSpan RetencaoDoProgresso = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Serviço de geocodificação em segundo plano iniciado.");

        await foreach (var unidadeId in fila.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessarAsync(unidadeId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Nunca deixar a fila morrer por causa de uma unidade.
                logger.LogError(ex, "Erro inesperado ao geocodificar a unidade {UnidadeId}.", unidadeId);
            }

            fila.LimparConcluidos(RetencaoDoProgresso);
        }

        logger.LogInformation("Serviço de geocodificação em segundo plano encerrado.");
    }

    private async Task ProcessarAsync(Guid unidadeId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var geocoder = scope.ServiceProvider.GetRequiredService<UnitGeocoder>();

        var unidade = await db.Locations.FirstOrDefaultAsync(l => l.Id == unidadeId, ct);
        if (unidade == null)
        {
            logger.LogWarning("Unidade {UnidadeId} não encontrada; ignorando.", unidadeId);
            return;
        }

        var loteId = unidade.LoteImportacao;

        unidade.StatusGeocodificacao = Models.StatusGeocodificacao.Processando;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Geocodificando a unidade {Unidade}.", unidade.Name);
        var resultado = await geocoder.GeocodificarAsync(unidade, ct: ct);
        await db.SaveChangesAsync(ct);

        fila.Concluir(loteId, resultado.Status);

        logger.LogInformation(
            "Geocodificação da unidade {Unidade} concluída com status {Status} (cache: {Cache}).",
            unidade.Name, resultado.Status, resultado.VeioDoCache);
    }
}
