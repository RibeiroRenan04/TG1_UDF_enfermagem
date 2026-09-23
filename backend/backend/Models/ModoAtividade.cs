namespace EstagioCheck.API.Models;

/// <summary>Decide a validação do ponto: presencial → localização, remoto → código, sem atividade → nada.</summary>
public static class ModoAtividade
{
    public const string Presencial = "presencial";
    public const string Remoto = "remoto";
    public const string SemAtividade = "sem_atividade";

    public static readonly string[] Todos = [Presencial, Remoto, SemAtividade];

    public static bool Valido(string? modo) => modo != null && Todos.Contains(modo);

    public static string? Normalizar(string? modo) => modo?.Trim().ToLowerInvariant() switch
    {
        "presencial" => Presencial,
        "remoto" or "remota" => Remoto,
        "sem_atividade" or "sem atividade" or "nenhuma" => SemAtividade,
        _ => null
    };

    public static string Rotulo(string? modo) => Normalizar(modo) switch
    {
        Presencial => "Presencial",
        Remoto => "Atividade remota",
        SemAtividade => "Sem atividade",
        _ => modo ?? "—"
    };
}

public static class ValidacaoPresenca
{
    public const string Localizacao = "localizacao";

    public const string Codigo = "codigo";

    public const string Nenhuma = "nenhuma";

    public static string De(string? modo) => ModoAtividade.Normalizar(modo) switch
    {
        ModoAtividade.Presencial => Localizacao,
        ModoAtividade.Remoto => Codigo,
        _ => Nenhuma
    };
}
