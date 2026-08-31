using EstagioCheck.API.Data;
using EstagioCheck.API.Services;
using EstagioCheck.API.Services.Geocoding;
using EstagioCheck.API.Services.Import;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

// Npgsql: garante que DateTime seja tratado como UTC (timestamptz no PostgreSQL)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ── Porta dinâmica (Railway injeta a variável PORT) ───────────────────────────
// Só sobrescreve a URL quando rodando no Railway; localmente usa o launchSettings.json.
var railwayPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(railwayPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");
}

// ── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── JWT Authentication ────────────────────────────────────────────────────────
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

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<GeoService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<CertificateService>();
builder.Services.AddScoped<BuscaSaudeService>();

// ── Geocodificação de unidades de saúde ───────────────────────────────────────
// Toda a aplicação depende de IGeocodingService: trocar o Nominatim por outro
// provedor é substituir esta única linha de registro.
builder.Services.Configure<GeocodingOptions>(
    builder.Configuration.GetSection(GeocodingOptions.SectionName));
builder.Services.AddSingleton<IAddressNormalizer, AddressNormalizer>();
builder.Services.AddScoped<IGeocodingService, NominatimGeocodingService>();
builder.Services.AddScoped<UnitGeocoder>();
builder.Services.AddScoped<PlanilhaUnidadesReader>();
builder.Services.AddScoped<UnidadeImportService>();

// Fila e processamento em segundo plano: a geocodificação em massa respeita o
// limite de ~1 req/s do Nominatim e por isso nunca roda dentro da requisição HTTP.
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

// ── CORS ──────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddCors(options =>
    options.AddPolicy("Angular", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

// ── Controllers + Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers();
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

// ── Auto-migrate on startup ───────────────────────────────────────────────────
var connStr = app.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrWhiteSpace(connStr))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// ── Swagger (disponível em todos os ambientes) ────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI();

// CORS deve vir antes de autenticação e controllers.
app.UseCors("Angular");

app.UseAuthentication();
app.UseAuthorization();

// ── Health checks ─────────────────────────────────────────────────────────────
app.MapGet("/", () => "API ONLINE");
app.MapGet("/health", () => Results.Ok("Healthy"));

app.MapControllers();
app.Run();
