using System.Net.Http.Headers;
using System.Net.Http.Json;
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
}
