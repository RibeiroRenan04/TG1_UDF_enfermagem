namespace EstagioCheck.API.Models;

/// <summary>
/// Turnos do estágio. O turno é a unidade de controle do ponto: cada aluno tem,
/// no máximo, um check-in e um check-out por turno, e pode estar alocado em
/// unidades diferentes em turnos diferentes — nunca duas vezes no mesmo turno.
/// </summary>
public static class Turnos
{
    public const string Manha = "manha";
    public const string Tarde = "tarde";
    public const string Noite = "noite";

    public static readonly string[] Validos = [Manha, Tarde, Noite];

    public static bool Valido(string? turno) => turno != null && Validos.Contains(turno);

    /// <summary>
    /// Normaliza a entrada para o identificador canônico ("Manhã", "MATUTINO" →
    /// "manha"). Devolve <c>null</c> quando o texto não corresponde a um turno.
    /// </summary>
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

    public static string Rotulo(string? turno) => Normalizar(turno) switch
    {
        Manha => "manhã",
        Tarde => "tarde",
        Noite => "noite",
        _ => turno ?? "—"
    };
}
