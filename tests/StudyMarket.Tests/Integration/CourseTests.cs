using System.Net;
using System.Net.Http.Json;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Courses;
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

        await creator.PutAsJsonAsync($"/api/lessons/{lessonId}", SampleLesson with { Flashcards = [], Quiz = [SampleLesson.Quiz![0]] });
        var lesson = await creator.GetFromJsonAsync<LessonDetails>($"/api/lessons/{lessonId}");

        Assert.Empty(lesson!.Flashcards);
        Assert.Single(lesson.Quiz);
        Assert.Equal(1, lesson.Quiz[0].CorrectOption); // autor widzi poprawne odpowiedzi
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
