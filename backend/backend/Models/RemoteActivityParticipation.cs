using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>
/// Participação do aluno em uma atividade remota — a presença do dia remoto.
///
/// O código comprova que o aluno acessou a atividade dentro do prazo; a resposta,
/// quando exigida, comprova que ele a realizou. Há no máximo uma participação por
/// aluno e atividade: o código vale uma única vez.
/// </summary>
public class RemoteActivityParticipation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RemoteActivityId { get; set; }
    public Guid StudentId { get; set; }

    public DateTime RegisteredAt { get; set; } = BrasiliaTime.Agora;

    /// <summary>Código digitado, guardado como enviado, para auditoria.</summary>
    public string CodeUsed { get; set; } = string.Empty;

    /// <summary>Entrega do aluno, quando a atividade exige tarefa.</summary>
    public string? TaskResponse { get; set; }

    /// <summary>
    /// Ponto gerado a partir da participação. É por ele que a carga horária da
    /// atividade remota entra no mesmo cálculo de horas do estágio presencial.
    /// </summary>
    public Guid? AttendanceRecordId { get; set; }

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;

    // Navigation
    public RemoteActivity RemoteActivity { get; set; } = null!;
    public ApplicationUser Student { get; set; } = null!;
}
