namespace EstagioCheck.API.Models;

/// <summary>
/// Papéis de acesso do sistema. "Professor" é o identificador histórico
/// <c>supervisor</c>; a coordenadora enxerga o mesmo que o professor, porém
/// sem qualquer permissão de escrita.
/// </summary>
public static class Roles
{
    public const string Aluno = "aluno";
    public const string Preceptor = "preceptor";
    /// <summary>Professor responsável — acesso total.</summary>
    public const string Supervisor = "supervisor";
    /// <summary>Secretaria/estagiária: mesma visão do professor, somente leitura.</summary>
    public const string Coordenadora = "coordenadora";

    /// <summary>Quem enxerga os painéis de gestão (o professor e a coordenadora).</summary>
    public const string Gestao = $"{Supervisor},{Coordenadora}";

    /// <summary>Quem acompanha alunos em campo, mais a gestão.</summary>
    public const string AcompanhamentoEGestao = $"{Preceptor},{Supervisor},{Coordenadora}";

    public static readonly string[] Todos = [Aluno, Preceptor, Supervisor, Coordenadora];

    /// <summary>Perfis que precisam aceitar o termo de responsabilidade de acesso.</summary>
    public static bool ExigeTermoResponsabilidade(string role) => role != Aluno;

    public static bool SomenteLeitura(string? role) => role == Coordenadora;
}
