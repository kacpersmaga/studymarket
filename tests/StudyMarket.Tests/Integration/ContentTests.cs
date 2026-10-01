using System.Net;
using System.Net.Http.Json;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Courses;
using StudyMarket.Api.Features.Progress;
using static StudyMarket.Tests.Integration.TestData;

namespace StudyMarket.Tests.Integration;

// Edycja treści kursu, walidacja materiałów, katalog i dostęp.
public class ContentTests(ApiFactory api)
{
    [Fact]
    public async Task Author_manages_modules_and_lessons_other_creator_cannot()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var other = await api.CreateClientAsync(Role.Creator);
        var (courseId, lessonId) = await CreateCourseAsync(creator, publish: false);
        var moduleId = (await creator.GetFromJsonAsync<CourseDetails>($"/api/courses/{courseId}", ApiFactory.Json))!.Modules.Single().Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/courses/{courseId}/modules", new ModuleRequest("Obcy", 2))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/api/lessons/{lessonId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await creator.PutAsJsonAsync($"/api/modules/{moduleId}", new ModuleRequest("Mol", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await creator.DeleteAsync($"/api/lessons/{lessonId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await creator.DeleteAsync($"/api/modules/{moduleId}")).StatusCode);

        var course = await creator.GetFromJsonAsync<CourseDetails>($"/api/courses/{courseId}", ApiFactory.Json);
        Assert.Empty(course!.Modules);
    }

    [Theory]
    [InlineData("quiz")]  // indeks poprawnej odpowiedzi spoza listy opcji
    [InlineData("video")] // niepoprawny adres filmu
    [InlineData("title")] // brak tytułu
    public async Task Invalid_lesson_is_rejected(string invalidPart)
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (_, lessonId) = await CreateCourseAsync(creator, publish: false);
        var lesson = invalidPart switch
        {
            "quiz" => SampleLesson with { Quiz = [new QuizQuestionRequest("Pytanie", ["A", "B"], 5)] },
            "video" => SampleLesson with { VideoUrl = "to-nie-jest-adres" },
            _ => SampleLesson with { Title = "" },
        };

        var response = await creator.PutAsJsonAsync($"/api/lessons/{lessonId}", lesson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_filters_by_level_and_grade()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (courseId, _) = await CreateCourseAsync(creator); // liceum, klasa 4

        var matching = await api.CreateClient().GetFromJsonAsync<List<CourseSummary>>(
            "/api/courses?level=SecondarySchool&grade=4", ApiFactory.Json);
        var otherGrade = await api.CreateClient().GetFromJsonAsync<List<CourseSummary>>(
            "/api/courses?level=SecondarySchool&grade=1", ApiFactory.Json);

        Assert.Contains(matching!, c => c.Id == courseId);
        Assert.DoesNotContain(otherGrade!, c => c.Id == courseId);
    }

    [Fact]
    public async Task Unpublished_course_leaves_catalog_but_buyers_keep_access()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (courseId, lessonId) = await CreateCourseAsync(creator);
        var buyer = await api.CreateClientAsync(Role.Student);
        await PurchaseAsync(api, buyer, courseId);

        await creator.PostAsync($"/api/courses/{courseId}/unpublish", null);

        var catalog = await api.CreateClient().GetFromJsonAsync<List<CourseSummary>>("/api/courses", ApiFactory.Json);
        Assert.DoesNotContain(catalog!, c => c.Id == courseId);
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync($"/api/lessons/{lessonId}")).StatusCode);
    }

    [Fact]
    public async Task Learning_features_require_purchase()
    {
        var (courseId, lessonId) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var stranger = await api.CreateClientAsync(Role.Student);

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/api/lessons/{lessonId}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync($"/api/lessons/{lessonId}/quiz", new QuizSubmission([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/courses/{courseId}/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/api/lessons/{lessonId}")).StatusCode);
    }
}
