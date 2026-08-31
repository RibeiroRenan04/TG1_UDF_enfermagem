using System.Collections.Concurrent;
using System.Threading.Channels;

namespace EstagioCheck.API.Services.Geocoding;

/// <summary>
/// Fila das unidades que ainda precisam ser geocodificadas, com o andamento de
/// cada importação.
///
/// A geocodificação em massa não pode acontecer dentro da requisição HTTP: a
/// política do Nominatim é de ~1 req/s, então 100 unidades levam quase dois
/// minutos. A importação enfileira e responde na hora; o
/// <see cref="GeocodingBackgroundService"/> consome no ritmo permitido.
/// </summary>
public class GeocodingQueue
{
    private readonly Channel<Guid> _fila = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<Guid, ProgressoImportacao> _progresso = new();

    public ChannelReader<Guid> Reader => _fila.Reader;

    /// <summary>Enfileira uma unidade. Vinculada a um lote quando veio de importação.</summary>
    public void Enfileirar(Guid unidadeId, Guid? loteId = null)
    {
        if (loteId.HasValue)
            _progresso.AddOrUpdate(loteId.Value,
                _ => new ProgressoImportacao { Total = 1 },
                (_, p) => { p.Total++; return p; });

        _fila.Writer.TryWrite(unidadeId);
    }

    /// <summary>Registra o desfecho de uma unidade no lote.</summary>
    public void Concluir(Guid? loteId, string status)
    {
        if (!loteId.HasValue) return;
        if (!_progresso.TryGetValue(loteId.Value, out var p)) return;
        p.Registrar(status);
    }

    public ProgressoImportacao? ObterProgresso(Guid loteId) =>
        _progresso.TryGetValue(loteId, out var p) ? p : null;

    /// <summary>Descarta lotes antigos para a memória não crescer sem limite.</summary>
    public void LimparConcluidos(TimeSpan idade)
    {
        var limite = DateTime.UtcNow - idade;
        foreach (var (id, p) in _progresso)
            if (p.Concluido && p.AtualizadoEm < limite)
                _progresso.TryRemove(id, out _);
    }

    /// <summary>Contadores de um lote de importação, para a barra de progresso.</summary>
    public class ProgressoImportacao
    {
        private readonly object _trava = new();

        public int Total { get; set; }
        public int Sucesso { get; private set; }
        public int RevisaoManual { get; private set; }
        public int NaoEncontrado { get; private set; }
        public int Erro { get; private set; }
        public DateTime AtualizadoEm { get; private set; } = DateTime.UtcNow;

        public int Processados => Sucesso + RevisaoManual + NaoEncontrado + Erro;
        public int Pendentes => Math.Max(0, Total - Processados);
        public bool Concluido => Processados >= Total;
        public int PercentualConcluido => Total == 0 ? 100 : (int)(100.0 * Processados / Total);

        internal void Registrar(string status)
        {
            lock (_trava)
            {
                switch (status)
                {
                    case Models.StatusGeocodificacao.Sucesso: Sucesso++; break;
                    case Models.StatusGeocodificacao.RevisaoManual: RevisaoManual++; break;
                    case Models.StatusGeocodificacao.NaoEncontrado: NaoEncontrado++; break;
                    default: Erro++; break;
                }
                AtualizadoEm = DateTime.UtcNow;
            }
        }
    }
}
