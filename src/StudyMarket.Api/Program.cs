using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Account;
using StudyMarket.Api.Features.Auth;
using StudyMarket.Api.Features.Community;
using StudyMarket.Api.Features.Courses;
using StudyMarket.Api.Features.Moderation;
using StudyMarket.Api.Features.Orders;
using StudyMarket.Api.Features.Progress;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(config.GetConnectionString("Default")));

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false; // zachowujemy oryginalne nazwy claimów z tokena ("sub", "role")
    options.TokenValidationParameters = Jwt.ValidationParameters(config);
});
builder.Services.AddAuthorization();

// Frontend działa pod innym adresem (np. localhost:3000), więc przeglądarka wymaga zgody CORS.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? []).AllowAnyHeader().AllowAnyMethod()));

// Ochrona przed botami: limit żądań na adres IP dla całego API i ostrzejszy dla logowania/rejestracji.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        PerIpLimit(http, config.GetValue<int>("RateLimit:RequestsPerMinute")));
    options.AddPolicy("auth", http => PerIpLimit(http, config.GetValue<int>("RateLimit:AuthRequestsPerMinute")));
});

// /health - sprawdza, czy API działa i ma połączenie z bazą. Używa go hosting / Docker do automatycznego
// restartu i monitoring do powiadomienia o awarii.
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddValidation();     // walidacja atrybutów [Required], [Range]... w Minimal API (.NET 10)
builder.Services.AddProblemDetails(); // błędy w standardowym formacie RFC 9457
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter())); // enumy jako tekst w JSON

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    // Informacja w dokumentacji, że API używa tokenów JWT (przycisk autoryzacji w Scalar).
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
    };
    document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
    return Task.CompletedTask;
}));

var app = builder.Build();

// Bez tajnego klucza każdy mógłby podpisać własny token (np. z rolą Admin) - lepiej, żeby aplikacja nie wystartowała.
// Klucz deweloperski jest tylko w appsettings.Development.json; w produkcji podaje się go zmienną środowiskową Jwt__Key.
if (Encoding.UTF8.GetByteCount(config["Jwt:Key"] ?? "") < 32)
    throw new InvalidOperationException("Brak klucza Jwt:Key (min. 32 bajty). Ustaw zmienną środowiskową Jwt__Key.");

// Schemat bazy tworzony/aktualizowany migracjami przy starcie aplikacji.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    if (app.Environment.IsDevelopment() && config.GetValue<bool>("SeedDemoData"))
        await DemoData.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>());
}

// Za reverse proxy (nginx, Cloudflare) wszystkie żądania przychodzą z adresu proxy. Bez tego limit żądań
// byłby wspólny dla wszystkich użytkowników. Włączać tylko, gdy API jest osiągalne wyłącznie przez proxy -
// inaczej klient mógłby podać fałszywy nagłówek X-Forwarded-For i ominąć limit.
if (config.GetValue<bool>("BehindReverseProxy"))
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();

app.MapHealthChecks("/health"); // "Healthy" (200) albo "Unhealthy" (503)
app.MapOpenApi();               // /openapi/v1.json
app.MapScalarApiReference();    // /scalar - interaktywna dokumentacja

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapCourseEndpoints();
app.MapContentEndpoints();
app.MapAttachmentEndpoints();
app.MapCommunityEndpoints();
app.MapOrderEndpoints();
app.MapPaymentEndpoints();
app.MapProgressEndpoints();
app.MapModerationEndpoints();

app.Run();

static RateLimitPartition<string> PerIpLimit(HttpContext http, int permitsPerMinute) =>
    RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1) });
