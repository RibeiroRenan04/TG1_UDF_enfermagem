using System.Security.Cryptography;
using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>
/// Atividade remota criada pelo professor. Nos dias em que parte da turma fica em
/// casa, a presença não pode depender de localização: ela é comprovada pelo código
/// da atividade e, quando o professor exigir, pela tarefa entregue.
///
/// O código sozinho não autoriza a presença. O registro confere ainda se o aluno
/// pertence ao grupo autorizado, se a atividade está aberta, se a janela de horário
/// ainda vale e se ele já não registrou participação.
/// </summary>
public class RemoteActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Turma/grupo autorizado. Só os alunos vinculados a ele registram presença.</summary>
    public Guid GroupId { get; set; }

    /// <summary>Rodízio de origem, quando a atividade substitui um dia programado.</summary>
    public Guid? ScheduleId { get; set; }

    public Guid ProfessorId { get; set; }

    public DateOnly ActivityDate { get; set; }

    /// <summary>Início da janela em que o código é aceito.</summary>
    public TimeOnly StartTime { get; set; } = new(8, 0);

    /// <summary>Fim da janela. Passado esse horário o código deixa de valer.</summary>
    public TimeOnly EndTime { get; set; } = new(12, 0);

    /// <summary>Carga horária creditada ao aluno que concluir a atividade.</summary>
    public double EstimatedHours { get; set; } = 4;

    /// <summary>Código de presença, ex: "ENF-7K92". Gerado pelo sistema.</summary>
    public string PresenceCode { get; set; } = string.Empty;

    /// <summary>A atividade exige uma entrega além do código.</summary>
    public bool RequiresTask { get; set; }

    /// <summary>Tipo da tarefa complementar. Ver <see cref="TipoTarefaRemota"/>.</summary>
    public string? TaskType { get; set; }

    /// <summary>O que o aluno precisa entregar, escrito pelo professor.</summary>
    public string? TaskInstructions { get; set; }

    /// <summary>
    /// Atividade encerrada manualmente pelo professor. Independe da janela de
    /// horário: encerrar invalida o código antes da hora.
    /// </summary>
    public bool Ativo { get; set; } = true;

    public DateTime CreatedAt { get; set; } = BrasiliaTime.Agora;
    public DateTime UpdatedAt { get; set; } = BrasiliaTime.Agora;

    // Navigation
    public StudentGroup Group { get; set; } = null!;
    public RotationSchedule? Schedule { get; set; }
    public ApplicationUser Professor { get; set; } = null!;
    public ICollection<RemoteActivityParticipation> Participations { get; set; } = [];

    public DateTime InicioEm => ActivityDate.ToDateTime(StartTime);
    public DateTime FimEm => ActivityDate.ToDateTime(EndTime);

    /// <summary>A janela do código está aberta neste momento.</summary>
    public bool AbertaEm(DateTime momento) =>
        Ativo && momento >= InicioEm && momento <= FimEm;

    /// <summary>
    /// Gera o código de presença: prefixo do curso e quatro caracteres sorteados.
    /// O alfabeto exclui 0/O e 1/I, que o aluno confunde ao digitar.
    /// </summary>
    public static string GerarCodigo(string prefixo = "ENF")
    {
        const string alfabeto = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var sufixo = string.Concat(Enumerable.Range(0, 4)
            .Select(_ => alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)]));
        return $"{prefixo}-{sufixo}";
    }

    /// <summary>Compara códigos ignorando caixa, espaços e o hífen digitado a mais ou a menos.</summary>
    public static string NormalizarCodigo(string? codigo) =>
        new((codigo ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

/// <summary>Tarefa complementar que a atividade remota pode exigir.</summary>
public static class TipoTarefaRemota
{
    public const string Questionario = "questionario";
    public const string Arquivo = "arquivo";
    public const string Discursiva = "discursiva";
    public const string EstudoDeCaso = "estudo_de_caso";
    public const string AulaOnline = "aula_online";
    public const string Leitura = "leitura";
    public const string Formulario = "formulario";

    public static readonly string[] Todos =
        [Questionario, Arquivo, Discursiva, EstudoDeCaso, AulaOnline, Leitura, Formulario];

    public static bool Valido(string? tipo) => tipo != null && Todos.Contains(tipo);

    /// <summary>Tarefas em que a entrega é um texto do aluno.</summary>
    public static bool ExigeResposta(string? tipo) =>
        tipo is Questionario or Discursiva or EstudoDeCaso or Formulario or Arquivo;

    public static string Rotulo(string? tipo) => tipo switch
    {
        Questionario => "Questionário",
        Arquivo => "Envio de arquivo",
        Discursiva => "Resposta discursiva",
        EstudoDeCaso => "Estudo de caso",
        AulaOnline => "Participação em aula on-line",
        Leitura => "Confirmação de leitura",
        Formulario => "Formulário de avaliação",
        _ => tipo ?? "—"
    };
}
