using Npgsql;

namespace EstagioCheck.API.Services.Seguranca;

/// <summary>
/// TLS verificado com o banco (ISO 27001 A.8.24). O Supabase assina os certificados com CA própria,
/// que não está no repositório de confiança do sistema; a raiz vai junto da aplicação em
/// <c>certs/</c>. Com <c>SSL Mode=VerifyFull</c> na connection string e sem <c>Root Certificate</c>,
/// usa-se essa raiz — assim a mesma string funciona local, no Railway e em qualquer pasta de execução.
/// </summary>
public static class ConexaoBanco
{
    public const string ArquivoCertificadoRaiz = "certs/supabase-root-2021-ca.crt";

    public static string? ComCertificadoRaiz(string? connectionString, string? pastaBase = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;

        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        if (csb.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull) || !string.IsNullOrEmpty(csb.RootCertificate))
            return connectionString;

        var caminho = Path.Combine(pastaBase ?? AppContext.BaseDirectory, ArquivoCertificadoRaiz);
        if (!File.Exists(caminho))
            throw new InvalidOperationException($"SSL Mode={csb.SslMode} exige o certificado raiz em '{caminho}'.");

        csb.RootCertificate = caminho;
        return csb.ConnectionString;
    }
}
