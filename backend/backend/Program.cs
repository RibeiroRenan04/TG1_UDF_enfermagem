using EstagioCheck.API.Data;
using EstagioCheck.API.Services;
using EstagioCheck.API.Services.Geocoding;
using EstagioCheck.API.Services.Import;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT Key is not configured.");

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
    });

builder.Services.AddAuthorization();

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
builder.Services.AddHttpContextAccessor();

var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddCors(options =>
    options.AddPolicy("Angular", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

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

app.UseExceptionHandler();

var connStr = app.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrWhiteSpace(connStr))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

// CORS deve vir antes de autenticação e controllers.
app.UseCors("Angular");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "API ONLINE");
app.MapGet("/health", () => Results.Ok("Healthy"));

app.MapControllers();
app.Run();
