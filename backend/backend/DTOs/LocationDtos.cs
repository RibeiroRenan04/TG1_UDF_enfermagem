using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

public class LocationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Address { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public int RadiusMeters { get; init; }
    public bool IsInstitution { get; init; }
    public string ShiftStart { get; init; } = string.Empty;
    public string ShiftEnd { get; init; } = string.Empty;
    public string? CodigoCnes { get; init; }
}

public record ImportBuscaSaudeDto(
    [Required] string CodigoCnes,
    [Required, MaxLength(300)] string Nome,
    [MaxLength(500)] string? Endereco,
    [Required] double Latitude,
    [Required] double Longitude
);
