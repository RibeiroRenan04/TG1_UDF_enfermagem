using EstagioCheck.API.Services;

namespace EstagioCheck.API.Models;

/// <summary>
/// Trilha de auditoria (ISO 27001 A.8.15 / LGPD art. 37): quem fez o quê, quando e de onde.
/// Só recebe INSERT — o banco recusa UPDATE (ver database/015).
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; } = BrasiliaTime.Agora;

    /// <summary>Nulo em ações anônimas (login recusado, recuperação de senha) e do próprio sistema.</summary>
    public Guid? UserId { get; set; }
    public string? UserRole { get; set; }

    /// <summary>"criacao" | "alteracao" | "exclusao" para dados; eventos nomeados (ex.: "login_sucesso") para o resto.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Tabela do banco (ex.: "RegistrosPresenca") ou área do evento (ex.: "Autenticacao").</summary>
    public string Entity { get; set; } = string.Empty;
    public string? EntityId { get; set; }

    /// <summary>JSON com os campos alterados (de/para) ou o contexto do evento. Nunca leva senha ou código.</summary>
    public string? Details { get; set; }

    public bool Success { get; set; } = true;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Mesmo valor do cabeçalho X-Correlation-Id: liga o registro aos logs da aplicação.</summary>
    public string? CorrelationId { get; set; }
}

public static class AcoesAuditoria
{
    public const string Criacao = "criacao";
    public const string Alteracao = "alteracao";
    public const string Exclusao = "exclusao";

    public const string LoginSucesso = "login_sucesso";
    public const string LoginFalha = "login_falha";
    public const string LoginBloqueado = "login_bloqueado";
    public const string LoginContaInativa = "login_conta_inativa";
    public const string PrimeiroAcesso = "primeiro_acesso";
    public const string TermoAceito = "termo_aceito";
    public const string RecuperacaoSolicitada = "recuperacao_senha_solicitada";
    public const string RecuperacaoCodigoInvalido = "recuperacao_senha_codigo_invalido";
    public const string SenhaRedefinida = "senha_redefinida";

    public const string ExportacaoDados = "lgpd_exportacao_dados";
    public const string Anonimizacao = "lgpd_anonimizacao";
}
