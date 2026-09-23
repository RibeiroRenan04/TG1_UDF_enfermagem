using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>A presença do dia remoto; uma por aluno e atividade.</summary>
public class RemoteActivityParticipation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RemoteActivityId { get; set; }
    public Guid StudentId { get; set; }

    public DateTime RegisteredAt { get; set; } = BrasiliaTime.Agora;

    /// <summary>Guardado como enviado, para auditoria.</summary>
    public string CodeUsed { get; set; } = string.Empty;

    public string? TaskResponse { get; set; }

    /// <summary>Ponto gerado, para a carga remota entrar no mesmo cálculo de horas do presencial.</summary>
    public Guid? AttendanceRecordId { get; set; }

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;

    public RemoteActivity RemoteActivity { get; set; } = null!;
    public ApplicationUser Student { get; set; } = null!;
}
