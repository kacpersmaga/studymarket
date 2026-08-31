using System.Net;
using System.Net.Http.Json;
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
    public async Task Wrong_password_is_rejected()
    {
        var user = ApiFactory.NewUser();
        await Register(user);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "zlehaslo"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
