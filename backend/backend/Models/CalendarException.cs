using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>
/// Data em que a programação normal não vale. Nunca é gravada aluno a aluno: tem uma
/// abrangência, e a programação aplica a mais específica que alcança o aluno.
/// </summary>
public class CalendarException
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Type { get; set; } = TipoExcecao.Feriado;

    public string Scope { get; set; } = AbrangenciaExcecao.Faculdade;

    public DateOnly StartDate { get; set; }

    /// <summary>Último dia da exceção. Em um feriado de um dia só, igual a <see cref="StartDate"/>.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Restringe a exceção a um turno; nulo vale para todos.</summary>
    public string? Shift { get; set; }

    public Guid? GroupId { get; set; }
    public Guid? ScheduleId { get; set; }
    public Guid? StudentId { get; set; }

    /// <summary>Unidade que passa a valer quando o tipo é troca de local.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Atividade remota que substitui o dia, quando o tipo é remoto.</summary>
    public Guid? RemoteActivityId { get; set; }

    public string Description { get; set; } = string.Empty;

    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    public StudentGroup? Group { get; set; }
    public RotationSchedule? Schedule { get; set; }
    public ApplicationUser? Student { get; set; }
    public Location? Location { get; set; }
    public RemoteActivity? RemoteActivity { get; set; }
    public ApplicationUser? CreatedBy { get; set; }

    public bool AlcancaData(DateOnly data) => StartDate <= data && data <= EndDate;
}

public static class TipoExcecao
{
    public const string Feriado = "feriado";
    public const string Recesso = "recesso";
    public const string Cancelado = "cancelado";
    public const string Remoto = "remoto";
    public const string TrocaLocal = "troca_local";
    public const string AtividadeEspecial = "atividade_especial";
    public const string Reposicao = "reposicao";

    public static readonly string[] Todos =
        [Feriado, Recesso, Cancelado, Remoto, TrocaLocal, AtividadeEspecial, Reposicao];

    public static bool Valido(string? tipo) => tipo != null && Todos.Contains(tipo);

    /// <summary>Tipos que dispensam o aluno do ponto naquele dia.</summary>
    public static bool DispensaPonto(string tipo) => tipo is Feriado or Recesso or Cancelado;

    public static string Rotulo(string? tipo) => tipo switch
    {
        Feriado => "Feriado",
        Recesso => "Recesso",
        Cancelado => "Estágio cancelado",
        Remoto => "Atividade remota",
        TrocaLocal => "Troca de local",
        AtividadeEspecial => "Atividade especial",
        Reposicao => "Reposição de atividade",
        _ => tipo ?? "—"
    };
}

/// <summary>Empate resolvido por <see cref="Especificidade"/>: aluno vence rodízio, que vence turma…</summary>
public static class AbrangenciaExcecao
{
    public const string Faculdade = "faculdade";
    public const string Turma = "turma";
    public const string Rodizio = "rodizio";
    public const string Aluno = "aluno";

    public static readonly string[] Todas = [Faculdade, Turma, Rodizio, Aluno];

    public static bool Valida(string? escopo) => escopo != null && Todas.Contains(escopo);

    public static int Especificidade(string? escopo) => escopo switch
    {
        Aluno => 3,
        Rodizio => 2,
        Turma => 1,
        _ => 0
    };

    public static string Rotulo(string? escopo) => escopo switch
    {
        Faculdade => "Toda a faculdade",
        Turma => "Turma",
        Rodizio => "Rodízio",
        Aluno => "Aluno específico",
        _ => escopo ?? "—"
    };
}
