namespace EstagioCheck.API.Services;

/// <summary>Horário oficial do sistema: Brasília (GMT-3). Todo horário é gravado e comparado neste fuso.</summary>
public static class BrasiliaTime
{
    // O Brasil não adota horário de verão desde 2019.
    public const int OffsetHoras = -3;

    public static readonly TimeSpan Offset = TimeSpan.FromHours(OffsetHoras);

    /// <summary>
    /// <see cref="DateTimeKind.Unspecified"/> de propósito: com <c>Utc</c> o JSON sairia com "Z" e o
    /// navegador converteria de novo, mostrando três horas a menos.
    /// </summary>
    public static DateTime Agora =>
        DateTime.SpecifyKind(DateTime.UtcNow + Offset, DateTimeKind.Unspecified);

    public static DateOnly Hoje => DateOnly.FromDateTime(Agora);

    public static DateTime DeUtc(DateTime utc) =>
        DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);

    public static DateTime ParaUtc(DateTime brasilia) =>
        DateTime.SpecifyKind(brasilia - Offset, DateTimeKind.Utc);
}
