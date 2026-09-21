using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Auth;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(StudyMarket.Tests.Integration.ApiFactory))]

namespace StudyMarket.Tests.Integration;

// Uruchamia całe API w pamięci, z prawdziwym PostgreSQL w kontenerze Dockera.
// Jedna instancja na wszystkie testy; testy nie kolidują, bo każdy tworzy własnych użytkowników.
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Haslo123!";
    public const string WebhookSecret = "whsec_test_secret";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18").Build();

    public string ConnectionString => _database.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();
        _ = Services; // start API (i migracji) raz, zanim równoległe testy zaczną z niego korzystać
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Stripe:WebhookSecret", WebhookSecret);
    }

    public static RegisterRequest NewUser(Role role = Role.Student) =>
        new($"{Guid.NewGuid()}@test.pl", Password, "Jan Test", role);

    // Rejestruje nowego użytkownika i zwraca klienta HTTP z jego tokenem.
    public async Task<HttpClient> CreateClientAsync(Role role)
    {
        var client = CreateClient();
        var user = NewUser(role == Role.Admin ? Role.Creator : role);
        var response = await client.PostAsJsonAsync("/api/auth/register", user, Json);
        response.EnsureSuccessStatusCode();

        if (role == Role.Admin) // administratora nie da się zarejestrować przez API - nadajemy rolę w bazie
        {
            await WithDbAsync(db => db.Users.Where(u => u.Email == user.Email)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Role, Role.Admin)));
            response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, Password));
        }

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // Wysyła zdarzenie tak, jak zrobiłby to Stripe: JSON + nagłówek z podpisem HMAC-SHA256.
    public Task<HttpResponseMessage> SendStripeEventAsync(int orderId, string secret = WebhookSecret,
        string type = "checkout.session.completed", string paymentStatus = "paid")
    {
        var json = """
            {"id":"evt_test","object":"event","api_version":"2025-01-01","type":"EVENT_TYPE",
             "data":{"object":{"id":"cs_test","object":"checkout.session","payment_status":"PAYMENT_STATUS","metadata":{"orderId":"ORDER_ID"}}}}
            """.Replace("ORDER_ID", orderId.ToString()).Replace("EVENT_TYPE", type).Replace("PAYMENT_STATUS", paymentStatus);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{json}")));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/stripe-webhook")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={signature}");
        return CreateClient().SendAsync(request);
    }
}
