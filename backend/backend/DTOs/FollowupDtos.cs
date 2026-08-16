using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

public class FollowupDto
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string? StudentRgm { get; init; }
    public Guid PreceptorId { get; init; }
    public string PreceptorName { get; init; } = string.Empty;
    public Guid? ScheduleId { get; init; }
    public Guid? GroupId { get; init; }
    public Guid? LocationId { get; init; }
    public string? LocationName { get; init; }
    public string? Shift { get; init; }
    public string? PeriodLabel { get; init; }
    public string? Semester { get; init; }
    public DateOnly? FollowUpStart { get; init; }
    public DateOnly? FollowUpEnd { get; init; }

    // Dimensões
    public string? PosturaPontualidade { get; init; }
    public string? PosturaEtica { get; init; }
    public string? PosturaResponsabilidade { get; init; }
    public string? ComunicacaoEquipe { get; init; }
    public string? ComunicacaoPaciente { get; init; }
    public string? ComunicacaoEscuta { get; init; }
    public string? OrganizacaoPlanejamento { get; init; }
    public string? OrganizacaoSeguranca { get; init; }
    public string? OrganizacaoRegistros { get; init; }
    public string? ParticipacaoIniciativa { get; init; }
    public string? ParticipacaoAprendizado { get; init; }
    public string? ParticipacaoAutocritica { get; init; }

    // Descritivos
    public string? Potencialidades { get; init; }
    public string? AspectosAprimorar { get; init; }
    public string? SituacoesRelevantes { get; init; }
    public string? ObservacoesDocente { get; init; }
    public string? EvolucaoSemanal { get; init; }

    // Status / assinaturas
    public string Status { get; init; } = string.Empty;
    public DateTime? PreceptorSignedAt { get; init; }
    public string? PreceptorSignedName { get; init; }
    public DateTime? StudentSignedAt { get; init; }
    public string? StudentSignedName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Retorno da busca de aluno por RGM. Traz os dados já cadastrados no sistema
/// (período, turno, semestre, turma e escala vigente) para preenchimento
/// automático do relatório, reduzindo digitação e divergência de dados.
/// </summary>
public class StudentLookupDto
{
    public Guid StudentId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Rgm { get; init; }
    public int? Semester { get; init; }
    public string? Shift { get; init; }
    public string? PeriodLabel { get; init; }
    public Guid? GroupId { get; init; }
    public string? GroupCode { get; init; }
    public string? GroupName { get; init; }
    public Guid? ScheduleId { get; init; }
    public Guid? LocationId { get; init; }
    public string? LocationName { get; init; }
    public string? ActivityType { get; init; }
    public DateOnly? FollowUpStart { get; init; }
    public DateOnly? FollowUpEnd { get; init; }
}

/// <summary>
/// Criação do acompanhamento. Herda os campos avaliativos de <see cref="UpdateFollowupDto"/>
/// para que o preceptor consiga salvar cabeçalho e conteúdo em uma única requisição.
/// </summary>
public class CreateFollowupDto : UpdateFollowupDto
{
    [Required] public Guid StudentId { get; init; }
    public Guid? ScheduleId { get; init; }
    public Guid? GroupId { get; init; }
    public Guid? LocationId { get; init; }
    public string? Shift { get; init; }
    public string? PeriodLabel { get; init; }
    public string? Semester { get; init; }
    public DateOnly? FollowUpStart { get; init; }
    public DateOnly? FollowUpEnd { get; init; }
}

public class UpdateFollowupDto
{
    public string? PosturaPontualidade { get; init; }
    public string? PosturaEtica { get; init; }
    public string? PosturaResponsabilidade { get; init; }
    public string? ComunicacaoEquipe { get; init; }
    public string? ComunicacaoPaciente { get; init; }
    public string? ComunicacaoEscuta { get; init; }
    public string? OrganizacaoPlanejamento { get; init; }
    public string? OrganizacaoSeguranca { get; init; }
    public string? OrganizacaoRegistros { get; init; }
    public string? ParticipacaoIniciativa { get; init; }
    public string? ParticipacaoAprendizado { get; init; }
    public string? ParticipacaoAutocritica { get; init; }
    public string? Potencialidades { get; init; }
    public string? AspectosAprimorar { get; init; }
    public string? SituacoesRelevantes { get; init; }
    public string? ObservacoesDocente { get; init; }
    public string? EvolucaoSemanal { get; init; }
}

public record FinalizeFollowupDto(
    [Required] string SignerName
);
