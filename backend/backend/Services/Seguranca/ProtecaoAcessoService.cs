using Microsoft.Extensions.Caching.Memory;

namespace EstagioCheck.API.Services.Seguranca;

/// <summary>
/// Trava por conta contra força bruta (ISO 27001 A.8.5): depois de <c>MaxTentativas</c> erros em
/// sequência, a chave (ex.: "login:email") fica bloqueada por <c>BloqueioMinutos</c>. Por conta e
/// não por IP porque os alunos de um campus saem pelo mesmo IP. Fica em memória: um reinício
/// zera as contagens, o que é aceitável para uma instância única.
/// </summary>
public class ProtecaoAcessoService(IMemoryCache cache, IConfiguration config)
{
    private int MaxTentativas => config.GetValue("Seguranca:MaxTentativas", 5);
    private TimeSpan Bloqueio => TimeSpan.FromMinutes(config.GetValue("Seguranca:BloqueioMinutos", 15));

    private sealed class Contagem
    {
        public int Falhas;
        public DateTimeOffset? BloqueadoAte;
    }

    public bool Bloqueado(string chave, out TimeSpan restante)
    {
        restante = TimeSpan.Zero;
        if (!cache.TryGetValue(Chave(chave), out Contagem? c) || c?.BloqueadoAte == null) return false;

        restante = c.BloqueadoAte.Value - DateTimeOffset.UtcNow;
        return restante > TimeSpan.Zero;
    }

    /// <summary>Conta uma tentativa malsucedida; ao atingir o limite, bloqueia a chave.</summary>
    public void RegistrarFalha(string chave)
    {
        var c = cache.GetOrCreate(Chave(chave), e =>
        {
            // A janela de contagem é a mesma do bloqueio: erros espaçados não se acumulam para sempre.
            e.AbsoluteExpirationRelativeToNow = Bloqueio;
            return new Contagem();
        })!;

        lock (c)
        {
            c.Falhas++;
            if (c.Falhas >= MaxTentativas)
            {
                c.BloqueadoAte = DateTimeOffset.UtcNow + Bloqueio;
                cache.Set(Chave(chave), c, Bloqueio);
            }
        }
    }

    public void Limpar(string chave) => cache.Remove(Chave(chave));

    public static string Minutos(TimeSpan restante) =>
        Math.Max(1, (int)Math.Ceiling(restante.TotalMinutes)).ToString();

    private static string Chave(string chave) => $"protecao-acesso:{chave.ToLowerInvariant()}";
}
