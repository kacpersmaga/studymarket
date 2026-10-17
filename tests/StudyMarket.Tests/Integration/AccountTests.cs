using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Account;
using StudyMarket.Api.Features.Auth;
using StudyMarket.Api.Features.Community;
using static StudyMarket.Tests.Integration.TestData;

namespace StudyMarket.Tests.Integration;

public class AccountTests(ApiFactory api)
{
    [Fact]
    public async Task User_can_export_own_data()
    {
        var (courseId, lessonId) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var student = await api.CreateClientAsync(Role.Student);
        await PurchaseAsync(api, student, courseId);
        await student.PostAsJsonAsync($"/api/lessons/{lessonId}/comments", new CommentRequest("Pytanie"));

        var export = await student.GetFromJsonAsync<DataExport>("/api/me/export", ApiFactory.Json);

        Assert.Equal(Role.Student, export!.Profile.Role);
        Assert.Single(export.Orders);
        Assert.Equal("Pytanie", export.Comments.Single().Text);
    }

    [Fact]
    public async Task Password_change_requires_current_password()
    {
        var student = await api.CreateClientAsync(Role.Student);
        var email = (await student.GetFromJsonAsync<ProfileDto>("/api/me", ApiFactory.Json))!.Email;

        var withWrongCurrent = await student.PutAsJsonAsync("/api/me/password", new ChangePasswordRequest("zlehaslo", "NoweHaslo456!"));
        var withCorrectCurrent = await student.PutAsJsonAsync("/api/me/password", new ChangePasswordRequest(ApiFactory.Password, "NoweHaslo456!"));
        var oldLogin = await api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, ApiFactory.Password));
        var newLogin = await api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "NoweHaslo456!"));

        Assert.Equal(HttpStatusCode.BadRequest, withWrongCurrent.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, withCorrectCurrent.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Deleted_account_is_anonymized_but_sales_records_are_kept()
    {
        var (courseId, lessonId) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var student = await api.CreateClientAsync(Role.Student);
        var profile = await student.GetFromJsonAsync<ProfileDto>("/api/me", ApiFactory.Json);
        var orderId = await PurchaseAsync(api, student, courseId);
        await student.PostAsJsonAsync($"/api/lessons/{lessonId}/comments", new CommentRequest("Mój komentarz"));

        Assert.Equal(HttpStatusCode.NoContent, (await student.DeleteAsync("/api/me")).StatusCode);

        var login = await api.CreateClient().PostAsJsonAsync("/api/auth/login",
            new StudyMarket.Api.Features.Auth.LoginRequest(profile!.Email, ApiFactory.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        await api.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == profile.Id);
            Assert.Equal("Usunięty użytkownik", user.DisplayName);
            Assert.DoesNotContain(profile.Email, user.Email);
            Assert.False(await db.Comments.AnyAsync(c => c.UserId == profile.Id));
            Assert.True(await db.Orders.AnyAsync(o => o.Id == orderId)); // dowód sprzedaży zostaje
        });
    }
}
