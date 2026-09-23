using System.Collections.Concurrent;
using System.Threading.Channels;

namespace EstagioCheck.API.Services.Geocoding;

/// <summary>
/// A ~1 req/s, 100 unidades levam quase dois minutos: a importação enfileira e responde na hora,
/// e o <see cref="GeocodingBackgroundService"/> consome no ritmo permitido.
/// </summary>
public class GeocodingQueue
{
    private readonly Channel<Guid> _fila = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<Guid, ProgressoImportacao> _progresso = new();

    public ChannelReader<Guid> Reader => _fila.Reader;

    public void Enfileirar(Guid unidadeId, Guid? loteId = null)
    {
        if (loteId.HasValue)
            _progresso.AddOrUpdate(loteId.Value,
                _ => new ProgressoImportacao { Total = 1 },
                (_, p) => { p.Total++; return p; });

        _fila.Writer.TryWrite(unidadeId);
    }

    public void Concluir(Guid? loteId, string status)
    {
        if (!loteId.HasValue) return;
        if (!_progresso.TryGetValue(loteId.Value, out var p)) return;
        p.Registrar(status);
    }

    public ProgressoImportacao? ObterProgresso(Guid loteId) =>
        _progresso.TryGetValue(loteId, out var p) ? p : null;

    public void LimparConcluidos(TimeSpan idade)
    {
        var limite = DateTime.UtcNow - idade;
        foreach (var (id, p) in _progresso)
            if (p.Concluido && p.AtualizadoEm < limite)
                _progresso.TryRemove(id, out _);
    }

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
