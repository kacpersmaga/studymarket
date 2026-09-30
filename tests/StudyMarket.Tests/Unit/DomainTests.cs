using System.Security.Claims;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features;
using StudyMarket.Api.Features.Progress;

namespace StudyMarket.Tests.Unit;

public class QuizScoringTests
{
    private readonly Lesson _lesson = new()
    {
        Title = "Ułamki",
        QuizQuestions =
        [
            new() { Id = 1, Text = "1/2 + 1/2 = ?", Options = ["1", "2"], CorrectOption = 0 },
            new() { Id = 2, Text = "1/4 * 2 = ?", Options = ["1/8", "1/2"], CorrectOption = 1 },
        ],
    };

    [Fact]
    public void Counts_only_correct_answers() =>
        Assert.Equal(1, _lesson.CountCorrectAnswers(new Dictionary<int, int> { [1] = 0, [2] = 0 }));

    [Fact]
    public void Missing_answers_are_counted_as_wrong() =>
        Assert.Equal(0, _lesson.CountCorrectAnswers(new Dictionary<int, int>()));
}

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

public class CourseProgressTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 3, 33)]
    [InlineData(4, 4, 100)]
    public void Percent_is_rounded_down(int completed, int total, int expected) =>
        Assert.Equal(expected, new CourseProgress(1, completed, total, []).Percent);
}

public class CommissionTests
{
    [Theory]
    [InlineData(100, 15, 15)]
    [InlineData(49.99, 15, 7.50)] // 7,4985 -> zaokrąglenie do groszy
    [InlineData(2, 15, 0.30)]
    public void Fee_is_rounded_to_grosze(decimal price, decimal percent, decimal expected) =>
        Assert.Equal(expected, Order.CalculateFee(price, percent));
}
