namespace EstagioCheck.API.Services;

/// <summary>
/// Horário oficial do sistema: Brasília (GMT-3, sem horário de verão).
///
/// Os registros de ponto são gravados e comparados neste fuso — e não em UTC —
/// porque toda a operação do estágio (turnos, dia letivo, atrasos) acontece no
/// horário local de Brasília. Guardar em UTC fazia o horário aparecer três horas
/// à frente nas telas e deslocava o dia dos registros feitos após as 21h.
/// </summary>
public static class BrasiliaTime
{
    /// <summary>Diferença fixa para UTC. O Brasil não adota horário de verão desde 2019.</summary>
    public const int OffsetHoras = -3;

    public static readonly TimeSpan Offset = TimeSpan.FromHours(OffsetHoras);

    /// <summary>
    /// Data e hora atuais em Brasília.
    ///
    /// O <see cref="DateTimeKind"/> é <c>Unspecified</c> de propósito: a coluna no banco
    /// é <c>timestamp without time zone</c> e o valor já está no fuso local. Se o Kind
    /// fosse <c>Utc</c>, o JSON sairia com o sufixo "Z" e o navegador converteria de novo,
    /// mostrando o ponto três horas atrás — e a resposta do POST ficaria diferente do GET,
    /// que lê a mesma linha do banco como <c>Unspecified</c>.
    /// </summary>
    public static DateTime Agora =>
        DateTime.SpecifyKind(DateTime.UtcNow + Offset, DateTimeKind.Unspecified);

    /// <summary>Data atual em Brasília (o "hoje" do estágio).</summary>
    public static DateOnly Hoje => DateOnly.FromDateTime(Agora);

    /// <summary>Converte um instante UTC para o horário de Brasília.</summary>
    public static DateTime DeUtc(DateTime utc) =>
        DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);

    /// <summary>Converte um horário de Brasília de volta para UTC.</summary>
    public static DateTime ParaUtc(DateTime brasilia) =>
        DateTime.SpecifyKind(brasilia - Offset, DateTimeKind.Utc);
}
