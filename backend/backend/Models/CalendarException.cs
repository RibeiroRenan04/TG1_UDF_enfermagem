using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>
/// Exceção do calendário: a data em que a programação normal do rodízio não vale.
/// Feriado, recesso, estágio cancelado, troca de local, dia que virou remoto,
/// atividade especial ou reposição.
///
/// A exceção nunca é gravada aluno a aluno: ela tem uma abrangência (faculdade,
/// curso, turma, rodízio ou um aluno específico) e a programação de cada data
/// aplica a mais específica que alcança aquele aluno.
/// </summary>
public class CalendarException
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tipo da exceção. Ver <see cref="TipoExcecao"/>.</summary>
    public string Type { get; set; } = TipoExcecao.Feriado;

    /// <summary>Abrangência. Ver <see cref="AbrangenciaExcecao"/>.</summary>
    public string Scope { get; set; } = AbrangenciaExcecao.Faculdade;

    public DateOnly StartDate { get; set; }

    /// <summary>Último dia da exceção. Em um feriado de um dia só, igual a <see cref="StartDate"/>.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Restringe a exceção a um turno; nulo vale para todos.</summary>
    public string? Shift { get; set; }

    // ── Alvo, conforme a abrangência ──────────────────────────────────────────
    public Guid? GroupId { get; set; }
    public Guid? ScheduleId { get; set; }
    public Guid? StudentId { get; set; }

    /// <summary>Curso alcançado quando a abrangência é "curso".</summary>
    public string? Course { get; set; }

    // ── Substituição da programação ───────────────────────────────────────────
    /// <summary>Unidade que passa a valer quando o tipo é troca de local.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Atividade remota que substitui o dia, quando o tipo é remoto.</summary>
    public Guid? RemoteActivityId { get; set; }

    public string Description { get; set; } = string.Empty;

    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    // Navigation
    public StudentGroup? Group { get; set; }
    public RotationSchedule? Schedule { get; set; }
    public ApplicationUser? Student { get; set; }
    public Location? Location { get; set; }
    public RemoteActivity? RemoteActivity { get; set; }
    public ApplicationUser? CreatedBy { get; set; }

    public bool AlcancaData(DateOnly data) => StartDate <= data && data <= EndDate;
}

/// <summary>O que a exceção faz com a programação daquela data.</summary>
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

/// <summary>
/// Quem a exceção alcança. A ordem em <see cref="Especificidade"/> resolve o
/// empate: a exceção do aluno vence a do rodízio, que vence a da turma, e assim
/// por diante.
/// </summary>
public static class AbrangenciaExcecao
{
    public const string Faculdade = "faculdade";
    public const string Curso = "curso";
    public const string Turma = "turma";
    public const string Rodizio = "rodizio";
    public const string Aluno = "aluno";

    public static readonly string[] Todas = [Faculdade, Curso, Turma, Rodizio, Aluno];

    public static bool Valida(string? escopo) => escopo != null && Todas.Contains(escopo);

    /// <summary>Quanto maior, mais específica — e mais forte na hora de decidir o dia.</summary>
    public static int Especificidade(string? escopo) => escopo switch
    {
        Aluno => 4,
        Rodizio => 3,
        Turma => 2,
        Curso => 1,
        _ => 0
    };

    public static string Rotulo(string? escopo) => escopo switch
    {
        Faculdade => "Toda a faculdade",
        Curso => "Curso",
        Turma => "Turma",
        Rodizio => "Rodízio",
        Aluno => "Aluno específico",
        _ => escopo ?? "—"
    };
}
