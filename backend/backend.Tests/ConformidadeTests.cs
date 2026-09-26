using EstagioCheck.API.Controllers;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using EstagioCheck.API.Services.Auditoria;
using EstagioCheck.API.Services.Privacidade;
using EstagioCheck.API.Services.Seguranca;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>Apoio dos testes de segurança, auditoria e LGPD.</summary>
internal static class Conformidade
{
    public static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "chave-de-teste-com-mais-de-32-bytes-0123456789",
            ["Seguranca:MaxTentativas"] = "5",
            ["Seguranca:BloqueioMinutos"] = "15",
        })
        .Build();

    public static IHttpContextAccessor Http(Guid? usuario = null, string? papel = null)
    {
        var http = new DefaultHttpContext { TraceIdentifier = "correlacao-teste" };
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.7");
        if (usuario != null)
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, usuario.Value.ToString()),
                new Claim(ClaimTypes.Role, papel ?? Roles.Supervisor)
            ], "teste"));
        return new HttpContextAccessor { HttpContext = http };
    }

    /// <summary>Contexto com o interceptor de auditoria, como em produção.</summary>
    public static AppDbContext ContextoAuditado(IHttpContextAccessor? http = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"conformidade_{Guid.NewGuid()}")
            .AddInterceptors(new AuditoriaInterceptor(http ?? Http()))
            .Options);

    public static AuthController Auth(AppDbContext db, ProtecaoAcessoService? protecao = null)
    {
        var config = Config();
        var http = Http();
        return new AuthController(
            db, new TokenService(config),
            new EmailService(config, TestSupport.Logger<EmailService>()),
            new AuditoriaService(db, http),
            protecao ?? new ProtecaoAcessoService(new MemoryCache(new MemoryCacheOptions()), config))
        {
            ControllerContext = new ControllerContext { HttpContext = http.HttpContext! }
        };
    }

    public static ApplicationUser ComSenha(ApplicationUser u, string senha)
    {
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(senha, workFactor: 4);
        return u;
    }

    public static int? Status(IActionResult r) => (r as IStatusCodeActionResult)?.StatusCode;
}

public class AuditoriaInterceptorTests
{
    [Fact]
    public async Task Criacao_registra_quem_quando_e_de_onde_mascarando_dado_pessoal()
    {
        var professor = Guid.NewGuid();
        using var db = Conformidade.ContextoAuditado(Conformidade.Http(professor, Roles.Supervisor));

        var aluno = TestSupport.Aluno("Maria da Silva", "11223344");
        db.Add(aluno);
        await db.SaveChangesAsync();

        var log = Assert.Single(db.AuditLogs);
        Assert.Equal(AcoesAuditoria.Criacao, log.Action);
        Assert.Equal("Usuarios", log.Entity);
        Assert.Equal(aluno.Id.ToString(), log.EntityId);
        Assert.Equal(professor, log.UserId);
        Assert.Equal(Roles.Supervisor, log.UserRole);
        Assert.Equal("10.0.0.7", log.IpAddress);
        Assert.Equal("correlacao-teste", log.CorrelationId);
        Assert.DoesNotContain("Maria", log.Details);
        Assert.DoesNotContain("11223344", log.Details);
        Assert.Contains(AuditoriaInterceptor.ValorMascarado, log.Details);
    }

    [Fact]
    public async Task Alteracao_guarda_so_os_campos_que_mudaram_com_de_e_para()
    {
        using var db = Conformidade.ContextoAuditado();
        var aluno = TestSupport.Aluno();
        db.Add(aluno);
        await db.SaveChangesAsync();

        aluno.Shift = "tarde";
        await db.SaveChangesAsync();

        var log = db.AuditLogs.Single(l => l.Action == AcoesAuditoria.Alteracao);
        using var json = JsonDocument.Parse(log.Details!);
        var campos = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["Turno"], campos);
        Assert.Equal("manha", json.RootElement.GetProperty("Turno").GetProperty("de").GetString());
        Assert.Equal("tarde", json.RootElement.GetProperty("Turno").GetProperty("para").GetString());
    }

    [Fact]
    public async Task Troca_de_senha_aparece_na_trilha_sem_o_hash()
    {
        using var db = Conformidade.ContextoAuditado();
        var aluno = Conformidade.ComSenha(TestSupport.Aluno(), "antiga123");
        db.Add(aluno);
        await db.SaveChangesAsync();

        aluno.PasswordHash = BCrypt.Net.BCrypt.HashPassword("nova12345", workFactor: 4);
        await db.SaveChangesAsync();

        var log = db.AuditLogs.Single(l => l.Action == AcoesAuditoria.Alteracao);
        Assert.Contains("SenhaHash", log.Details);
        Assert.DoesNotContain("$2", log.Details);
    }

    [Fact]
    public async Task Exclusao_e_registrada_e_codigo_de_senha_fica_fora_da_trilha()
    {
        using var db = Conformidade.ContextoAuditado();
        var unidade = TestSupport.Unidade();
        db.Add(unidade);
        db.PasswordResetCodes.Add(new PasswordResetCode { Email = "a@b.c", Code = "123456", ExpiresAt = BrasiliaTime.Agora });
        await db.SaveChangesAsync();

        db.Remove(unidade);
        await db.SaveChangesAsync();

        Assert.Equal(
            [AcoesAuditoria.Criacao, AcoesAuditoria.Exclusao],
            db.AuditLogs.OrderBy(l => l.Id).Select(l => l.Action).ToList());
        Assert.DoesNotContain(db.AuditLogs, l => l.Entity == "CodigosRedefinicaoSenha");
    }

    [Fact]
    public async Task Gravacao_sem_mudanca_real_nao_gera_registro()
    {
        using var db = Conformidade.ContextoAuditado();
        var aluno = TestSupport.Aluno();
        db.Add(aluno);
        await db.SaveChangesAsync();

        aluno.Shift = aluno.Shift;
        db.Entry(aluno).State = EntityState.Modified;
        await db.SaveChangesAsync();

        Assert.Single(db.AuditLogs);
    }
}

public class LoginSegurancaTests
{
    [Fact]
    public async Task Login_valido_e_auditado()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = Conformidade.ComSenha(TestSupport.Aluno(), "Senha1234");
        db.Add(aluno);
        await db.SaveChangesAsync();

        var r = await Conformidade.Auth(db).Login(new LoginDto(aluno.Email!, "Senha1234"));

        Assert.IsType<OkObjectResult>(r.Result);
        var log = Assert.Single(db.AuditLogs);
        Assert.Equal(AcoesAuditoria.LoginSucesso, log.Action);
        Assert.Equal(aluno.Id, log.UserId);
    }

    [Fact]
    public async Task Senha_errada_e_auditada_sem_expor_o_email_inteiro()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = Conformidade.ComSenha(TestSupport.Aluno(), "Senha1234");
        aluno.Email = "maria.silva@cs.udf.edu.br";
        db.Add(aluno);
        await db.SaveChangesAsync();

        var r = await Conformidade.Auth(db).Login(new LoginDto(aluno.Email, "errada"));

        Assert.IsType<UnauthorizedObjectResult>(r.Result);
        var log = Assert.Single(db.AuditLogs);
        Assert.Equal(AcoesAuditoria.LoginFalha, log.Action);
        Assert.False(log.Success);
        Assert.DoesNotContain("maria.silva", log.Details);
        Assert.Contains("ma***@cs.udf.edu.br", log.Details);
    }

    [Fact]
    public async Task Cinco_erros_bloqueiam_a_conta_mesmo_com_a_senha_certa()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = Conformidade.ComSenha(TestSupport.Aluno(), "Senha1234");
        db.Add(aluno);
        await db.SaveChangesAsync();
        var auth = Conformidade.Auth(db);

        for (var i = 0; i < 5; i++)
            await auth.Login(new LoginDto(aluno.Email!, "errada"));

        var r = await auth.Login(new LoginDto(aluno.Email!, "Senha1234"));

        Assert.Equal(StatusCodes.Status429TooManyRequests, Conformidade.Status(r.Result!));
        Assert.Contains(db.AuditLogs, l => l.Action == AcoesAuditoria.LoginBloqueado);
    }

    [Fact]
    public async Task Bloqueio_de_uma_conta_nao_afeta_outra()
    {
        using var db = TestSupport.NovoContexto();
        var a = Conformidade.ComSenha(TestSupport.Aluno("A", "1"), "Senha1234");
        var b = Conformidade.ComSenha(TestSupport.Aluno("B", "2"), "Senha1234");
        db.AddRange(a, b);
        await db.SaveChangesAsync();
        var auth = Conformidade.Auth(db);

        for (var i = 0; i < 5; i++)
            await auth.Login(new LoginDto(a.Email!, "errada"));

        Assert.IsType<OkObjectResult>((await auth.Login(new LoginDto(b.Email!, "Senha1234"))).Result);
    }

    [Fact]
    public async Task Conta_inativa_nao_entra()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = Conformidade.ComSenha(TestSupport.Aluno(), "Senha1234");
        aluno.IsActive = false;
        db.Add(aluno);
        await db.SaveChangesAsync();

        var r = await Conformidade.Auth(db).Login(new LoginDto(aluno.Email!, "Senha1234"));

        Assert.Equal(StatusCodes.Status403Forbidden, Conformidade.Status(r.Result!));
        Assert.Equal(AcoesAuditoria.LoginContaInativa, Assert.Single(db.AuditLogs).Action);
    }

    [Fact]
    public async Task Token_de_conta_desativada_deixa_de_valer()
    {
        using var db = TestSupport.NovoContexto();
        var ativo = TestSupport.Aluno("Ativo", "1");
        var inativo = TestSupport.Aluno("Inativo", "2");
        inativo.IsActive = false;
        db.AddRange(ativo, inativo);
        await db.SaveChangesAsync();
        var cache = new MemoryCache(new MemoryCacheOptions());

        Assert.True(await ValidacaoUsuarioAtivo.EstaAtivoAsync(ativo.Id, cache, db));
        Assert.False(await ValidacaoUsuarioAtivo.EstaAtivoAsync(inativo.Id, cache, db));
        Assert.False(await ValidacaoUsuarioAtivo.EstaAtivoAsync(Guid.NewGuid(), cache, db));
    }
}

public class RecuperacaoSenhaSegurancaTests
{
    private static async Task<(AppDbContext Db, ApplicationUser Aluno)> ComCodigo(string codigo = "654321")
    {
        var db = TestSupport.NovoContexto();
        var aluno = Conformidade.ComSenha(TestSupport.Aluno(rgm: "55667788"), "Antiga123");
        db.Add(aluno);
        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            Email = aluno.Email!, Code = codigo, ExpiresAt = BrasiliaTime.Agora.AddMinutes(15)
        });
        await db.SaveChangesAsync();
        return (db, aluno);
    }

    [Fact]
    public async Task Codigo_fica_bloqueado_apos_cinco_erros_contra_forca_bruta()
    {
        var (db, aluno) = await ComCodigo();
        using var _ = db;
        var auth = Conformidade.Auth(db);

        for (var i = 0; i < 5; i++)
            Assert.IsType<BadRequestObjectResult>(
                await auth.VerifyResetCode(new VerifyResetCodeDto(aluno.Email!, $"00000{i}")));

        var certo = await auth.VerifyResetCode(new VerifyResetCodeDto(aluno.Email!, "654321"));
        Assert.Equal(StatusCodes.Status429TooManyRequests, Conformidade.Status(certo));
    }

    [Fact]
    public async Task Senha_nova_precisa_seguir_a_politica()
    {
        var (db, aluno) = await ComCodigo();
        using var _ = db;

        var r = await Conformidade.Auth(db).ResetPassword(
            new ResetPasswordDto(aluno.Email!, "654321", "x55667788y"));

        Assert.IsType<BadRequestObjectResult>(r);
        Assert.True(BCrypt.Net.BCrypt.Verify("Antiga123", aluno.PasswordHash));
    }

    [Fact]
    public async Task Redefinicao_valida_troca_a_senha_consome_o_codigo_e_audita()
    {
        var (db, aluno) = await ComCodigo();
        using var _ = db;

        var r = await Conformidade.Auth(db).ResetPassword(
            new ResetPasswordDto(aluno.Email!, "654321", "NovaSenha2026"));

        Assert.IsType<OkObjectResult>(r);
        Assert.True(BCrypt.Net.BCrypt.Verify("NovaSenha2026", aluno.PasswordHash));
        Assert.True(db.PasswordResetCodes.Single().Used);
        Assert.Contains(db.AuditLogs, l => l.Action == AcoesAuditoria.SenhaRedefinida && l.UserId == aluno.Id);
    }
}

public class PoliticaSenhaTests
{
    [Theory]
    [InlineData("curta1")]
    [InlineData("somenteletras")]
    [InlineData("1234567890")]
    public void Recusa_senha_fraca(string senha) => Assert.NotNull(PoliticaSenha.Validar(senha));

    [Fact]
    public void Recusa_senha_que_contem_o_rgm() =>
        Assert.NotNull(PoliticaSenha.Validar("abc12345678", rgm: "12345678"));

    [Fact]
    public void Recusa_senha_que_contem_o_login() =>
        Assert.NotNull(PoliticaSenha.Validar("Joao.Santos2026", email: "joao.santos@cs.udf.edu.br"));

    [Fact]
    public void Aceita_senha_adequada() =>
        Assert.Null(PoliticaSenha.Validar("Enfermagem2026", rgm: "12345678", email: "joao.santos@cs.udf.edu.br"));
}

public class PrivacidadeTests
{
    [Fact]
    public async Task Exportacao_traz_cadastro_e_registros_do_titular()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno("Maria", "998877");
        db.Add(aluno);
        db.AttendanceRecords.Add(new AttendanceRecord { StudentId = aluno.Id, Latitude = -15.7, Longitude = -47.8 });
        await db.SaveChangesAsync();

        var dados = await new PrivacidadeService(db).ExportarAsync(aluno.Id);

        var json = JsonSerializer.Serialize(dados);
        Assert.Contains("Maria", json);
        Assert.Contains("998877", json);
        Assert.Contains("-15.7", json);
    }

    [Fact]
    public async Task Exportacao_de_titular_inexistente_devolve_nulo() =>
        Assert.Null(await new PrivacidadeService(TestSupport.NovoContexto()).ExportarAsync(Guid.NewGuid()));

    [Fact]
    public async Task Nao_anonimiza_conta_ativa_nem_a_propria()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        db.Add(aluno);
        await db.SaveChangesAsync();
        var servico = new PrivacidadeService(db);

        Assert.Equal(StatusAnonimizacao.Recusada, (await servico.AnonimizarAsync(aluno.Id, Guid.NewGuid())).Status);
        aluno.IsActive = false;
        Assert.Equal(StatusAnonimizacao.Recusada, (await servico.AnonimizarAsync(aluno.Id, aluno.Id)).Status);
        Assert.Equal(StatusAnonimizacao.NaoEncontrado, (await servico.AnonimizarAsync(Guid.NewGuid(), aluno.Id)).Status);
    }

    [Fact]
    public async Task Anonimizacao_remove_identificacao_e_preserva_o_registro_academico()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = Conformidade.ComSenha(TestSupport.Aluno("Maria da Silva", "998877"), "Senha1234");
        aluno.Phone = "61999990000";
        aluno.IsActive = false;
        db.Add(aluno);
        var ponto = new AttendanceRecord
        {
            StudentId = aluno.Id, Latitude = -15.7, Longitude = -47.8, PhotoUrl = "foto.jpg", Status = "aprovado"
        };
        db.AttendanceRecords.Add(ponto);
        await db.SaveChangesAsync();

        var (status, _) = await new PrivacidadeService(db).AnonimizarAsync(aluno.Id, Guid.NewGuid());
        await db.SaveChangesAsync();

        Assert.Equal(StatusAnonimizacao.Concluida, status);
        Assert.StartsWith(PrivacidadeService.PrefixoNomeAnonimizado, aluno.FullName);
        Assert.Null(aluno.Email);
        Assert.Null(aluno.Rgm);
        Assert.Null(aluno.Phone);
        Assert.False(BCrypt.Net.BCrypt.Verify("Senha1234", aluno.PasswordHash));
        Assert.Equal(0, ponto.Latitude);
        Assert.Null(ponto.PhotoUrl);
        // O ponto continua contando horas.
        Assert.Equal("aprovado", db.AttendanceRecords.Single().Status);
    }

    [Fact]
    public void Mascara_de_email_identifica_sem_expor() =>
        Assert.Equal("jo***@cs.udf.edu.br", Mascara.Email("joao.santos@cs.udf.edu.br"));
}

public class RetencaoDadosTests
{
    [Fact]
    public async Task Expurga_codigos_vencidos_e_auditoria_antiga_e_mantem_o_recente()
    {
        using var db = TestSupport.NovoContexto();
        var agora = new DateTime(2026, 9, 25, 12, 0, 0);
        db.PasswordResetCodes.AddRange(
            new PasswordResetCode { Email = "a@b.c", Code = "111111", ExpiresAt = agora.AddDays(-3) },
            new PasswordResetCode { Email = "a@b.c", Code = "222222", ExpiresAt = agora.AddMinutes(10) });
        db.AuditLogs.AddRange(
            new AuditLog { Action = "x", Entity = "y", OccurredAt = agora.AddDays(-400) },
            new AuditLog { Action = "x", Entity = "y", OccurredAt = agora.AddDays(-10) });
        await db.SaveChangesAsync();

        var (codigos, logs) = await RetencaoDadosService.ExpurgarAsync(db, agora, retencaoAuditoriaDias: 365);

        Assert.Equal(1, codigos);
        Assert.Equal(1, logs);
        Assert.Equal("222222", db.PasswordResetCodes.Single().Code);
        Assert.Equal(agora.AddDays(-10), db.AuditLogs.Single().OccurredAt);
    }
}

public class EmailDesativadoTests
{
    [Fact]
    public async Task Recuperacao_de_senha_responde_503_sem_gerar_codigo_quando_o_smtp_esta_desligado()
    {
        using var db = TestSupport.NovoContexto();
        var aluno = TestSupport.Aluno();
        db.Add(aluno);
        await db.SaveChangesAsync();

        var r = await Conformidade.Auth(db).ForgotPassword(new ForgotPasswordDto(aluno.Email!));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Conformidade.Status(r));
        Assert.Empty(db.PasswordResetCodes);
    }

    [Fact]
    public async Task Envio_direto_e_recusado_com_o_servico_desligado()
    {
        var email = new EmailService(Conformidade.Config(), TestSupport.Logger<EmailService>());

        Assert.False(email.Habilitado);
        await Assert.ThrowsAsync<InvalidOperationException>(() => email.SendResetCodeAsync("a@b.c", "123456"));
    }
}

public class ConexaoBancoTests
{
    private const string Base = "Host=db;Database=postgres;Username=u;Password=p";

    [Fact]
    public void Sem_verificacao_a_string_fica_como_esta() =>
        Assert.Equal($"{Base};SSL Mode=Require", ConexaoBanco.ComCertificadoRaiz($"{Base};SSL Mode=Require"));

    [Fact]
    public void VerifyFull_usa_a_ca_do_supabase_que_vai_junto_da_aplicacao()
    {
        var cs = ConexaoBanco.ComCertificadoRaiz($"{Base};SSL Mode=VerifyFull")!;

        var raiz = new Npgsql.NpgsqlConnectionStringBuilder(cs).RootCertificate!;
        Assert.EndsWith("supabase-root-2021-ca.crt", raiz);
        Assert.True(File.Exists(raiz));
    }

    [Fact]
    public void Certificado_informado_na_string_prevalece() =>
        Assert.Contains("minha-ca.crt",
            ConexaoBanco.ComCertificadoRaiz($"{Base};SSL Mode=VerifyFull;Root Certificate=/etc/minha-ca.crt"));

    [Fact]
    public void Sem_o_arquivo_da_ca_a_aplicacao_nao_sobe_com_verificacao() =>
        Assert.Throws<InvalidOperationException>(() =>
            ConexaoBanco.ComCertificadoRaiz($"{Base};SSL Mode=VerifyFull", pastaBase: Path.GetTempPath() + Guid.NewGuid()));
}
