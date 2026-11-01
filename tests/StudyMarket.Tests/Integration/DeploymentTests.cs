using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StudyMarket.Api.Data;
using StudyMarket.Api.Features.Auth;
using StudyMarket.Api.Features.Courses;

namespace StudyMarket.Tests.Integration;

// Zachowanie aplikacji w konfiguracji produkcyjnej.
public class DeploymentTests(ApiFactory api)
{
    [Fact]
    public void App_refuses_to_start_in_production_without_jwt_key()
    {
        using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", api.ConnectionString).UseSetting("SeedDemoData", "false"));

        var error = Assert.Throws<InvalidOperationException>(() => production.CreateClient());
        Assert.Contains("Jwt:Key", error.Message);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy_api_and_database() =>
        Assert.Equal("Healthy", await api.CreateClient().GetStringAsync("/health"));

    [Fact]
    public async Task Demo_data_provides_an_account_for_each_role_and_a_catalog()
    {
        var emptyDatabase = new NpgsqlConnectionStringBuilder(api.ConnectionString) { Database = "demo" }.ConnectionString;
        using var demo = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", emptyDatabase)
            .UseSetting("SeedDemoData", "true"));
        var client = demo.CreateClient();

        foreach (var email in new[] { "admin@studymarket.dev", "tworca@studymarket.dev", "uczen@studymarket.dev" })
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, DemoData.Password))).StatusCode);
        var catalog = await client.GetFromJsonAsync<List<CourseSummary>>("/api/courses", ApiFactory.Json);
        Assert.Equal(2, catalog!.Count); // szkic nie jest widoczny
    }

    [Fact]
    public async Task Behind_proxy_each_client_ip_has_its_own_limit()
    {
        using var behindProxy = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", api.ConnectionString).UseSetting("SeedDemoData", "false")
            .UseSetting("BehindReverseProxy", "true")
            .UseSetting("RateLimit:AuthRequestsPerMinute", "1"));
        var client = behindProxy.CreateClient();

        async Task<HttpStatusCode> LoginFrom(string ip)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest("nikt@test.pl", "haslo")),
            };
            request.Headers.Add("X-Forwarded-For", ip);
            return (await client.SendAsync(request)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFrom("203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginFrom("203.0.113.1")); // ten sam klient - zablokowany
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFrom("203.0.113.2"));    // inny klient - bez wpływu
    }
}
