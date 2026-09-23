namespace EstagioCheck.API.Models;

/// <summary>O número segue <see cref="System.DayOfWeek"/> (0 = domingo), como no banco.</summary>
public static class DiasSemana
{
    public const int Sexta = 5;

    public static readonly int[] Uteis = [1, 2, 3, 4, 5];

    public static bool Valido(int dia) => dia is >= 0 and <= 6;

    public static string Rotulo(int dia) => dia switch
    {
        0 => "Domingo",
        1 => "Segunda-feira",
        2 => "Terça-feira",
        3 => "Quarta-feira",
        4 => "Quinta-feira",
        5 => "Sexta-feira",
        6 => "Sábado",
        _ => "—"
    };
}
