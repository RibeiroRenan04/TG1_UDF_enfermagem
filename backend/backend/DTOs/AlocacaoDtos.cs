using System.ComponentModel.DataAnnotations;

namespace EstagioCheck.API.DTOs;

public class AlocacaoDto
{
    public Guid Id { get; init; }
    public Guid UnidadeId { get; init; }
    public string UnidadeNome { get; init; } = string.Empty;
    public string? UnidadeCidade { get; init; }
    public Guid EstagiarioId { get; init; }
    public string EstagiarioNome { get; init; } = string.Empty;
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

/// <summary>Os totais contam todas as alocações que atendem aos filtros, não só as da página.</summary>
public class AlocacoesPaginaDto
{
    public List<AlocacaoDto> Itens { get; init; } = [];
    public int Total { get; init; }
    public int Ativas { get; init; }
    public int Pagina { get; init; }
    public int TamanhoPagina { get; init; }
}

public class EstagiarioDisponivelDto
{
    public Guid Id { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string? Rgm { get; init; }
    public string? Email { get; init; }
    public int? Semestre { get; init; }
    public string? Turno { get; init; }
    public string? Turma { get; init; }
    public Guid? UnidadeAtualId { get; init; }
    public string? UnidadeAtualNome { get; init; }
    /// <summary>Para a tela avisar que o turno já está ocupado antes de gravar.</summary>
    public List<AlocacaoPorTurnoDto> AlocacoesAtivas { get; init; } = [];
    public List<string> TurnosDisponiveis { get; init; } = [];
}

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
    // Troca explícita: encerra a alocação ativa do aluno naquele turno e cria esta.
    bool EncerrarAlocacaoAtual = false,
    // Em branco, usa o turno cadastrado do aluno.
    [MaxLength(10)] string? Turno = null
);

public record EncerrarAlocacaoDto(
    DateOnly? DataFim,
    [MaxLength(1000)] string? Observacao
);
