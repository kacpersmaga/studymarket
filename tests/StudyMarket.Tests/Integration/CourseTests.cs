using System.Net;
using System.Net.Http.Json;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Courses;
using StudyMarket.Api.Features.Progress;
using static StudyMarket.Tests.Integration.TestData;

namespace StudyMarket.Tests.Integration;

public class CourseTests(ApiFactory api)
{
    [Fact]
    public async Task Student_cannot_create_course()
    {
        var student = await api.CreateClientAsync(Role.Student);
        var response = await student.PostAsJsonAsync("/api/courses", SampleCourse, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_course_data_is_rejected()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var response = await creator.PostAsJsonAsync("/api/courses", SampleCourse with { Title = "", Price = 1 }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Course_without_lessons_cannot_be_published()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var course = await (await creator.PostAsJsonAsync("/api/courses", SampleCourse, ApiFactory.Json))
            .Content.ReadFromJsonAsync<CourseSummary>(ApiFactory.Json);

        var response = await creator.PostAsync($"/api/courses/{course!.Id}/publish", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_author_or_admin_can_edit_course()
    {
        var (courseId, _) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var otherCreator = await api.CreateClientAsync(Role.Creator);
        var admin = await api.CreateClientAsync(Role.Admin);

        var byOther = await otherCreator.PutAsJsonAsync($"/api/courses/{courseId}", SampleCourse, ApiFactory.Json);
        var byAdmin = await admin.PutAsJsonAsync($"/api/courses/{courseId}", SampleCourse, ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Forbidden, byOther.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byAdmin.StatusCode);
    }

    [Fact]
    public async Task Course_details_tell_frontend_which_button_to_show()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (courseId, _) = await CreateCourseAsync(creator);
        var buyer = await api.CreateClientAsync(Role.Student);
        await PurchaseAsync(api, buyer, courseId);

        async Task<(bool, bool)> FlagsFor(HttpClient client)
        {
            var details = await client.GetFromJsonAsync<CourseDetails>($"/api/courses/{courseId}", ApiFactory.Json);
            return (details!.CanEdit, details.HasAccess);
        }

        Assert.Equal((false, false), await FlagsFor(api.CreateClient()));                       // gość: "Kup"
        Assert.Equal((false, false), await FlagsFor(await api.CreateClientAsync(Role.Student))); // zalogowany: "Kup"
        Assert.Equal((false, true), await FlagsFor(buyer));                                     // kupujący: "Ucz się"
        Assert.Equal((true, true), await FlagsFor(creator));                                    // autor: "Edytuj"
    }

    [Fact]
    public async Task Draft_course_is_hidden_from_others()
    {
        var (courseId, _) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator), publish: false);
        var response = await api.CreateClient().GetAsync($"/api/courses/{courseId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_search_finds_narrow_topic_in_description()
    {
        var (courseId, _) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var found = await api.CreateClient().GetFromJsonAsync<List<CourseSummary>>("/api/courses?search=krok po kroku", ApiFactory.Json);
        Assert.Contains(found!, c => c.Id == courseId);
    }

    [Fact]
    public async Task Updating_lesson_replaces_its_materials()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (_, lessonId) = await CreateCourseAsync(creator);

        await creator.PutAsJsonAsync($"/api/lessons/{lessonId}", SampleLesson with { Flashcards = [], Exercises = [], Quiz = [SampleLesson.Quiz![0]] });
        var lesson = await creator.GetFromJsonAsync<LessonDetails>($"/api/lessons/{lessonId}");

        Assert.Empty(lesson!.Flashcards);
        Assert.Empty(lesson.Exercises);
        Assert.Single(lesson.Quiz);
        Assert.Equal(1, lesson.Quiz[0].CorrectOption); // autor widzi poprawne odpowiedzi
    }

    [Fact]
    public async Task Purchase_gives_access_to_content_and_progress_tracking()
    {
        var (courseId, lessonId) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var student = await api.CreateClientAsync(Role.Student);

        // Kurs jest w katalogu, ale bez zakupu treść jest niedostępna.
        var catalog = await student.GetFromJsonAsync<List<CourseSummary>>("/api/courses?subject=Chemia", ApiFactory.Json);
        Assert.Contains(catalog!, c => c.Id == courseId);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/lessons/{lessonId}")).StatusCode);

        // Samo zamówienie (bez potwierdzenia płatności) nie daje dostępu.
        var order = await OrderAsync(student, courseId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/lessons/{lessonId}")).StatusCode);

        // Po potwierdzeniu płatności przez Stripe - pełna lekcja, ale bez poprawnych odpowiedzi quizu.
        (await api.SendStripeEventAsync(order.Id)).EnsureSuccessStatusCode();
        var lesson = await student.GetFromJsonAsync<LessonDetails>($"/api/lessons/{lessonId}");
        Assert.All(lesson!.Quiz, q => Assert.Null(q.CorrectOption));
        Assert.Equal("https://youtu.be/abc", lesson.VideoUrl);
        Assert.Equal("36 g / 18 g/mol = 2 mol", lesson.Exercises.Single().Solution);

        // Quiz: jedna z dwóch odpowiedzi poprawna.
        var answers = new QuizSubmission(new() { [lesson.Quiz[0].Id] = 1, [lesson.Quiz[1].Id] = 1 });
        var result = await (await student.PostAsJsonAsync($"/api/lessons/{lessonId}/quiz", answers)).Content.ReadFromJsonAsync<QuizResult>();
        Assert.Equal(new QuizResult(1, 2, 50), result);

        // Ukończenie jedynej lekcji = 100% kursu.
        await student.PostAsync($"/api/lessons/{lessonId}/complete", null);
        var progress = await student.GetFromJsonAsync<CourseProgress>($"/api/courses/{courseId}/progress");
        Assert.Equal(100, progress!.Percent);
        Assert.Equal(50, progress.Lessons.Single().BestQuizScore);
    }

    [Fact]
    public async Task Course_with_orders_cannot_be_deleted()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (courseId, _) = await CreateCourseAsync(creator);
        await OrderAsync(await api.CreateClientAsync(Role.Student), courseId);

        var response = await creator.DeleteAsync($"/api/courses/{courseId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
