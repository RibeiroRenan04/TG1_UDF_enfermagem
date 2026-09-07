namespace EstagioCheck.API.Models;

/// <summary>
/// O que o aluno deve fazer em um dia de estágio. É este valor que decide como o
/// ponto é validado: presencial confere a localização, remoto confere o código da
/// atividade, e sem atividade não gera obrigação de ponto.
/// </summary>
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

/// <summary>Como a presença do dia é comprovada.</summary>
public static class ValidacaoPresenca
{
    /// <summary>Geofence: o aluno precisa estar dentro do raio da unidade.</summary>
    public const string Localizacao = "localizacao";

    /// <summary>Código da atividade remota (e, quando houver, a tarefa).</summary>
    public const string Codigo = "codigo";

    /// <summary>Não há ponto a registrar (feriado, recesso, fim de semana).</summary>
    public const string Nenhuma = "nenhuma";

    public static string De(string? modo) => ModoAtividade.Normalizar(modo) switch
    {
        ModoAtividade.Presencial => Localizacao,
        ModoAtividade.Remoto => Codigo,
        _ => Nenhuma
    };
}
