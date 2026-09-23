using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>Regra de um dia da semana do rodízio ("sexta → faculdade"). Sem regras, dias úteis presenciais.</summary>
public class RotationDaySchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleId { get; set; }

    /// <summary>Mesmo número de <see cref="System.DayOfWeek"/> (0 = domingo).</summary>
    public int DayOfWeek { get; set; }

    public string Mode { get; set; } = ModoAtividade.Presencial;

    /// <summary>Nulo herda o local principal do rodízio. Ignorado em dia remoto ou sem atividade.</summary>
    public Guid? LocationId { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;

    public RotationSchedule Schedule { get; set; } = null!;
    public Location? Location { get; set; }
}
