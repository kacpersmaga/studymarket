using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Auth;

namespace StudyMarket.Tests.Integration;

public class AuthTests(ApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();

    private Task<HttpResponseMessage> Register(RegisterRequest user) => _client.PostAsJsonAsync("/api/auth/register", user, ApiFactory.Json);

    [Fact]
    public async Task Registered_user_can_log_in()
    {
        var user = ApiFactory.NewUser();
        await Register(user);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, user.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token));
    }

    [Fact]
    public async Task Email_must_be_unique()
    {
        var user = ApiFactory.NewUser();
        await Register(user);
        Assert.Equal(HttpStatusCode.Conflict, (await Register(user)).StatusCode);
    }

    [Fact]
    public async Task Cannot_register_as_admin() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Register(ApiFactory.NewUser(Role.Admin))).StatusCode);

    [Fact]
    public async Task Terms_must_be_accepted() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Register(ApiFactory.NewUser() with { AcceptTerms = false })).StatusCode);

    [Theory]
    [InlineData(Role.Student, 12, HttpStatusCode.BadRequest)] // poniżej 13 lat
    [InlineData(Role.Student, 14, HttpStatusCode.OK)]         // niepełnoletni może kupować i się uczyć
    [InlineData(Role.Creator, 17, HttpStatusCode.BadRequest)] // ale nie może sprzedawać
    [InlineData(Role.Creator, 18, HttpStatusCode.OK)]
    public async Task Age_limits_are_enforced(Role role, int age, HttpStatusCode expected) =>
        Assert.Equal(expected, (await Register(ApiFactory.NewUser(role, age))).StatusCode);

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var user = ApiFactory.NewUser();
        await Register(user);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "zlehaslo"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Too_many_login_attempts_are_blocked()
    {
        // Osobna instancja API z niskim limitem (ta sama baza), żeby nie blokować pozostałych testów.
        using var strictApi = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.ConnectionString).UseSetting("SeedDemoData", "false")
            .UseSetting("RateLimit:AuthRequestsPerMinute", "3"));
        var client = strictApi.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("bot@test.pl", "zgaduje"))).StatusCode);

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Unauthorized, 3), HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
    }
}
