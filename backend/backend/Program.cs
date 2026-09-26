using EstagioCheck.API.Data;
using EstagioCheck.API.Services;
using EstagioCheck.API.Services.Geocoding;
using EstagioCheck.API.Services.Import;
using EstagioCheck.API.Services.Auditoria;
using EstagioCheck.API.Services.Privacidade;
using EstagioCheck.API.Services.Seguranca;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

// DateTime vai ao banco sem conversão de fuso: todo horário já está em Brasília (BrasiliaTime).
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Só sobrescreve a URL quando rodando no Railway; localmente usa o launchSettings.json.
var railwayPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(railwayPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");
}

// Toda gravação do EF passa pela trilha de auditoria (ver AuditoriaInterceptor).
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AuditoriaInterceptor>();
var connectionString = ConexaoBanco.ComCertificadoRaiz(builder.Configuration.GetConnectionString("DefaultConnection"));
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseNpgsql(connectionString)
           .AddInterceptors(sp.GetRequiredService<AuditoriaInterceptor>()));

var jwtKey = builder.Configuration["Jwt:Key"];
// HMAC-SHA256 exige chave de 256 bits; chave curta é quebrável por força bruta offline.
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key não configurada ou com menos de 32 bytes.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
        // Conta desativada perde o acesso em até 1 minuto, sem esperar o token expirar.
        options.Events = new JwtBearerEvents { OnTokenValidated = ValidacaoUsuarioAtivo.AoValidarToken };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<ProtecaoAcessoService>();
builder.Services.AddScoped<AuditoriaService>();
builder.Services.AddScoped<PrivacidadeService>();
builder.Services.AddHostedService<RetencaoDadosService>();
builder.Services.AddLimitesRequisicao(builder.Configuration);

// Railway/Vercel terminam o TLS no proxy: sem isto o IP registrado seria o do proxy
// e o esquema seria sempre http. ForwardLimit = 1 confia só no último salto (o proxy da plataforma).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
    o.ForwardLimit = 1;
});
builder.Services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<GeoService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<CertificateService>();
builder.Services.AddScoped<BuscaSaudeService>();
builder.Services.AddScoped<ProgramacaoService>();
builder.Services.AddScoped<ConflitoTurmasService>();
builder.Services.AddScoped<PainelGestaoService>();
builder.Services.AddScoped<IrregularidadesPainelService>();

// Toda a aplicação depende de IGeocodingService: trocar o Nominatim por outro
// provedor é substituir esta única linha de registro.
builder.Services.Configure<GeocodingOptions>(
    builder.Configuration.GetSection(GeocodingOptions.SectionName));
builder.Services.AddSingleton<IAddressNormalizer, AddressNormalizer>();
builder.Services.AddScoped<IGeocodingService, NominatimGeocodingService>();
builder.Services.AddScoped<UnitGeocoder>();
builder.Services.AddScoped<PlanilhaUnidadesReader>();
builder.Services.AddScoped<UnidadeImportService>();

// A geocodificação em massa roda em segundo plano para respeitar o limite de ~1 req/s do Nominatim.
builder.Services.AddSingleton<GeocodingQueue>();
builder.Services.AddHostedService<GeocodingBackgroundService>();
// O User-Agent identifica a aplicação, como exige a política de uso do Nominatim.
var geocodingOptions = builder.Configuration
    .GetSection(GeocodingOptions.SectionName).Get<GeocodingOptions>() ?? new GeocodingOptions();

builder.Services.AddHttpClient(NominatimGeocodingService.HttpClientName, client =>
{
    client.BaseAddress = new Uri(geocodingOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(geocodingOptions.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(geocodingOptions.UserAgent);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("Accept-Language", geocodingOptions.AcceptLanguage);
});

builder.Services.AddHttpClient("BuscaSaude", client =>
{
    // Paginamos várias páginas do CNES em sequência, então damos uma folga maior.
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddCors(options =>
    options.AddPolicy("Angular", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              // O front pode mostrar o código ao usuário para ele informar ao suporte.
              .WithExposedHeaders(CorrelacaoMiddleware.Cabecalho, "Retry-After")));

builder.Services.AddScoped<PendenciasService>();
builder.Services.AddScoped<EscopoPreceptorService>();

// Toda resposta de erro sai como { message, errors? } (ver ErrosApi).
builder.Services.AddExceptionHandler<TratadorErrosApi>();
builder.Services.AddProblemDetails();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ErrosApi.RespostaValidacao);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "EstagioCheck API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Informe: Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelacaoMiddleware>();
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseMiddleware<CabecalhosSegurancaMiddleware>();

var connStr = app.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrWhiteSpace(connStr))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Em produção o Swagger expõe o mapa completo da API; só liga com Swagger:Habilitado=true.
if (!app.Environment.IsProduction() || app.Configuration.GetValue("Swagger:Habilitado", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// CORS deve vir antes de autenticação e controllers.
app.UseCors("Angular");

// Depois do CORS, para a resposta 429 chegar legível ao navegador.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

var versao = typeof(Program).Assembly
    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
    .FirstOrDefault()?.InformationalVersion ?? "desconhecida";

app.MapGet("/", () => "API ONLINE");
// Liveness: o processo responde. É o que o Railway consulta para reiniciar o serviço.
app.MapGet("/health", () => Results.Ok("Healthy"));
// Readiness: o banco responde. Para monitoramento externo do SLA (ver docs/SLA_SUPORTE.md).
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
{
    var inicio = System.Diagnostics.Stopwatch.StartNew();
    bool banco;
    try { banco = await db.Database.CanConnectAsync(ct); }
    catch { banco = false; }

    var corpo = new { status = banco ? "Healthy" : "Unhealthy", banco, latenciaMs = inicio.ElapsedMilliseconds, versao };
    return banco ? Results.Ok(corpo) : Results.Json(corpo, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapControllers();
app.Run();
