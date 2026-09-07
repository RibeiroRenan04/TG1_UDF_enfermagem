using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>
/// Regra de um dia da semana dentro do rodízio ("segunda → UBS, presencial";
/// "sexta → faculdade"; "sexta → atividade remota").
///
/// É o que permite cadastrar o rodízio uma vez e o sistema gerar sozinho a
/// programação de cada data do período, em vez de cadastrar data a data.
/// Um rodízio sem nenhuma regra continua valendo como antes: dias úteis
/// presenciais no local principal da escala.
/// </summary>
public class RotationDaySchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleId { get; set; }

    /// <summary>Dia da semana, no mesmo número de <see cref="System.DayOfWeek"/> (0 = domingo).</summary>
    public int DayOfWeek { get; set; }

    /// <summary>"presencial" | "remoto" | "sem_atividade". Ver <see cref="ModoAtividade"/>.</summary>
    public string Mode { get; set; } = ModoAtividade.Presencial;

    /// <summary>
    /// Unidade daquele dia. Nulo herda o local principal do rodízio — é o caso
    /// comum; preenchido quando o dia acontece em outro lugar (a faculdade, por
    /// exemplo). Ignorado quando o dia é remoto ou sem atividade.
    /// </summary>
    public Guid? LocationId { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;

    // Navigation
    public RotationSchedule Schedule { get; set; } = null!;
    public Location? Location { get; set; }
}
