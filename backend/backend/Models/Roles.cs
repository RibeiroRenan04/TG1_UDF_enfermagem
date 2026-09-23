namespace EstagioCheck.API.Models;

/// <summary>"Professor" é o identificador histórico <c>supervisor</c>.</summary>
public static class Roles
{
    public const string Aluno = "aluno";
    public const string Preceptor = "preceptor";
    public const string Supervisor = "supervisor";
    /// <summary>Mesma visão do professor, somente leitura.</summary>
    public const string Coordenadora = "coordenadora";

    public const string Gestao = $"{Supervisor},{Coordenadora}";

    public const string AcompanhamentoEGestao = $"{Preceptor},{Supervisor},{Coordenadora}";

    public static readonly string[] Todos = [Aluno, Preceptor, Supervisor, Coordenadora];

    public static bool ExigeTermoResponsabilidade(string role) => role != Aluno;

    public static bool SomenteLeitura(string? role) => role == Coordenadora;
}
