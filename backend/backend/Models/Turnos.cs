namespace EstagioCheck.API.Models;

/// <summary>O turno é a unidade de controle do ponto e da alocação.</summary>
public static class Turnos
{
    public const string Manha = "manha";
    public const string Tarde = "tarde";
    public const string Noite = "noite";

    public static readonly string[] Validos = [Manha, Tarde, Noite];

    public static bool Valido(string? turno) => turno != null && Validos.Contains(turno);

    /// <summary>"Manhã", "MATUTINO" → "manha"; <c>null</c> quando não é um turno.</summary>
    public static string? Normalizar(string? turno) => turno?.Trim().ToLowerInvariant() switch
    {
        "manha" or "manhã" or "matutino" => Manha,
        "tarde" or "vespertino" => Tarde,
        "noite" or "noturno" => Noite,
        _ => null
    };

    /// <summary>Turno correspondente à hora do dia, quando não há escala vinculada.</summary>
    public static string DaHora(int hora) =>
        hora is >= 6 and < 13 ? Manha :
        hora is >= 13 and < 19 ? Tarde : Noite;

    public static string DaHora(DateTime momento) => DaHora(momento.Hour);

    /// <summary>
    /// A unidade guarda um horário só (em geral o da manhã): medir o ponto da tarde contra ele
    /// marcava tudo como "fora do turno". Os outros turnos usam esta janela.
    /// </summary>
    public static (TimeSpan Inicio, TimeSpan Fim) JanelaPadrao(string turno) => Normalizar(turno) switch
    {
        Tarde => (new TimeSpan(13, 0, 0), new TimeSpan(19, 0, 0)),
        Noite => (new TimeSpan(19, 0, 0), new TimeSpan(23, 0, 0)),
        _ => (new TimeSpan(7, 0, 0), new TimeSpan(13, 0, 0))
    };

    public static (TimeSpan Inicio, TimeSpan Fim)? JanelaNaUnidade(string? inicioUnidade, string? fimUnidade, string turno)
    {
        if (TimeSpan.TryParse(inicioUnidade, out var inicio) && TimeSpan.TryParse(fimUnidade, out var fim)
            && DaHora(inicio.Hours) == Normalizar(turno))
            return (inicio, fim);

        return Normalizar(turno) == null ? null : JanelaPadrao(turno);
    }

    public static string Rotulo(string? turno) => Normalizar(turno) switch
    {
        Manha => "manhã",
        Tarde => "tarde",
        Noite => "noite",
        _ => turno ?? "—"
    };
}
