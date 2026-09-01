using System.Security.Claims;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features;

namespace StudyMarket.Tests.Unit;

public class AccessTests
{
    private readonly Course _course = new() { AuthorId = 7, Title = "Matura", Subject = "Matematyka" };

    private static ClaimsPrincipal UserWith(string id, Role role) =>
        new(new ClaimsIdentity([new Claim("sub", id), new Claim("role", role.ToString())], "test", "sub", "role"));

    [Fact]
    public void Author_can_edit() => Assert.True(UserWith("7", Role.Creator).CanEdit(_course));

    [Fact]
    public void Admin_can_edit_any_course() => Assert.True(UserWith("99", Role.Admin).CanEdit(_course));

    [Fact]
    public void Other_creator_cannot_edit() => Assert.False(UserWith("8", Role.Creator).CanEdit(_course));

    [Fact]
    public void Anonymous_user_cannot_edit() => Assert.False(new ClaimsPrincipal(new ClaimsIdentity()).CanEdit(_course));
}

