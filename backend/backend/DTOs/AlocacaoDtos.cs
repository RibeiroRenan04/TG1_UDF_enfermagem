using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

/// <summary>Alocação de um estagiário a uma unidade de saúde.</summary>
public class AlocacaoDto
{
    public Guid Id { get; init; }
    public Guid UnidadeId { get; init; }
    public string UnidadeNome { get; init; } = string.Empty;
    public string? UnidadeCidade { get; init; }
    public Guid EstagiarioId { get; init; }
    public string EstagiarioNome { get; init; } = string.Empty;
    /// <summary>Turno da alocação — um aluno pode ter uma por turno.</summary>
    public string Turno { get; init; } = string.Empty;
    public string? EstagiarioRgm { get; init; }
    public string? EstagiarioEmail { get; init; }
    public int? EstagiarioSemestre { get; init; }
    public string? EstagiarioTurno { get; init; }
    public DateOnly DataInicio { get; init; }
    public DateOnly? DataFim { get; init; }
    public bool Ativo { get; init; }
    public string? Observacao { get; init; }
    public string? CriadoPorNome { get; init; }
    public DateTime CriadoEm { get; init; }
}

/// <summary>Aluno disponível para alocação, na busca da tela da unidade.</summary>
public class EstagiarioDisponivelDto
{
    public Guid Id { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? Rgm { get; init; }
    public string? Email { get; init; }
    public int? Semestre { get; init; }
    public string? Turno { get; init; }
    public string? Turma { get; init; }
    /// <summary>Unidade em que já está alocado, se houver.</summary>
    public Guid? UnidadeAtualId { get; init; }
    public string? UnidadeAtualNome { get; init; }
    /// <summary>
    /// Alocações ativas do aluno, uma por turno. A tela usa esta lista para avisar
    /// que o turno escolhido já está ocupado antes de tentar gravar.
    /// </summary>
    public List<AlocacaoPorTurnoDto> AlocacoesAtivas { get; init; } = [];
    /// <summary>Turnos ainda livres para este aluno.</summary>
    public List<string> TurnosDisponiveis { get; init; } = [];
}

/// <summary>Alocação ativa de um aluno em um turno.</summary>
public class AlocacaoPorTurnoDto
{
    public string Turno { get; init; } = string.Empty;
    public Guid UnidadeId { get; init; }
    public string UnidadeNome { get; init; } = string.Empty;
}

public record CriarAlocacaoDto(
    [Required(ErrorMessage = "Informe o estagiário.")] Guid EstagiarioId,
    DateOnly? DataInicio,
    [MaxLength(1000)] string? Observacao,
    // Encerra a alocação ativa do estagiário NAQUELE TURNO e cria esta. Sem isso,
    // alocar quem já tem unidade no turno é recusado — trocar precisa ser uma
    // decisão explícita. Alocações em outros turnos não são tocadas.
    bool EncerrarAlocacaoAtual = false,
    // Turno da alocação ("manha" | "tarde" | "noite"). Em branco, usa o turno
    // cadastrado do aluno.
    [MaxLength(10)] string? Turno = null
);

public record EncerrarAlocacaoDto(
    DateOnly? DataFim,
    [MaxLength(1000)] string? Observacao
);
