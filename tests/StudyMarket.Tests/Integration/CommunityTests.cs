using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Community;
using StudyMarket.Api.Features.Courses;
using StudyMarket.Api.Features.Moderation;
using static StudyMarket.Tests.Integration.TestData;

namespace StudyMarket.Tests.Integration;

public class CommunityTests(ApiFactory api)
{
    [Fact]
    public async Task Only_buyers_can_comment_and_author_is_marked()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (courseId, lessonId) = await CreateCourseAsync(creator);
        var buyer = await api.CreateClientAsync(Role.Student);
        var stranger = await api.CreateClientAsync(Role.Student);
        await PurchaseAsync(api, buyer, courseId);

        var byStranger = await stranger.PostAsJsonAsync($"/api/lessons/{lessonId}/comments", new CommentRequest("Spam!"));
        await buyer.PostAsJsonAsync($"/api/lessons/{lessonId}/comments", new CommentRequest("Skąd 18 g/mol?"));
        await creator.PostAsJsonAsync($"/api/lessons/{lessonId}/comments", new CommentRequest("2·1 + 16 = 18"));
        var comments = await buyer.GetFromJsonAsync<List<CommentDto>>($"/api/lessons/{lessonId}/comments");

        Assert.Equal(HttpStatusCode.Forbidden, byStranger.StatusCode);
        Assert.Equal([false, true], comments!.Select(c => c.IsCourseAuthor));
    }

    [Fact]
    public async Task Only_buyers_can_review_and_rating_appears_in_catalog()
    {
        var (courseId, _) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var buyer1 = await api.CreateClientAsync(Role.Student);
        var buyer2 = await api.CreateClientAsync(Role.Student);
        await PurchaseAsync(api, buyer1, courseId);
        await PurchaseAsync(api, buyer2, courseId);

        var byStranger = await (await api.CreateClientAsync(Role.Student)).PutAsJsonAsync($"/api/courses/{courseId}/review", new ReviewRequest(1, "Fałszywa"));
        await buyer1.PutAsJsonAsync($"/api/courses/{courseId}/review", new ReviewRequest(5, null));
        await buyer2.PutAsJsonAsync($"/api/courses/{courseId}/review", new ReviewRequest(2, null));
        await buyer2.PutAsJsonAsync($"/api/courses/{courseId}/review", new ReviewRequest(4, "Po namyśle dobre")); // nadpisuje

        var course = await api.CreateClient().GetFromJsonAsync<CourseDetails>($"/api/courses/{courseId}", ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Forbidden, byStranger.StatusCode);
        Assert.Equal(2, course!.ReviewCount);
        Assert.Equal(4.5, course.AverageRating);
    }

    [Fact]
    public async Task Attachment_is_downloadable_only_after_purchase()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (courseId, lessonId) = await CreateCourseAsync(creator);
        var pdf = new ByteArrayContent("%PDF-1.7 arkusz"u8.ToArray());
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var upload = await creator.PostAsync($"/api/lessons/{lessonId}/attachments", new MultipartFormDataContent { { pdf, "file", "arkusz.pdf" } });
        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentDto>();
        var student = await api.CreateClientAsync(Role.Student);

        var beforePurchase = await student.GetAsync($"/api/attachments/{attachment!.Id}");
        await PurchaseAsync(api, student, courseId);
        var afterPurchase = await student.GetAsync($"/api/attachments/{attachment.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, beforePurchase.StatusCode);
        Assert.Equal("%PDF-1.7 arkusz", await afterPurchase.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Signed_link_works_without_token_and_cannot_be_forged()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (courseId, lessonId) = await CreateCourseAsync(creator);
        var png = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        await creator.PostAsync($"/api/lessons/{lessonId}/attachments", new MultipartFormDataContent { { png, "file", "wykres.png" } });
        var student = await api.CreateClientAsync(Role.Student);
        await PurchaseAsync(api, student, courseId);

        var url = (await student.GetFromJsonAsync<LessonDetails>($"/api/lessons/{lessonId}"))!.Attachments.Single().Url;
        var anonymous = api.CreateClient(); // tak jak <img src="..."> - przeglądarka nie wysyła tokenu

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url[..^1] + (url[^1] == '0' ? '1' : '0'))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url.Replace("expires=", "expires=9"))).StatusCode);
    }

    [Fact]
    public async Task Disallowed_file_type_is_rejected()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (_, lessonId) = await CreateCourseAsync(creator);
        var exe = new ByteArrayContent([0x4D, 0x5A]);
        exe.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await creator.PostAsync($"/api/lessons/{lessonId}/attachments", new MultipartFormDataContent { { exe, "file", "wirus.exe" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reported_content_reaches_admin_only()
    {
        var (courseId, _) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var student = await api.CreateClientAsync(Role.Student);
        var admin = await api.CreateClientAsync(Role.Admin);

        await student.PostAsJsonAsync("/api/reports", new ReportRequest(ReportTarget.Course, courseId, "Skany cudzego podręcznika"), ApiFactory.Json);
        var byStudent = await student.GetAsync("/api/admin/reports");
        var reports = await admin.GetFromJsonAsync<List<ReportDto>>("/api/admin/reports", ApiFactory.Json);
        var report = reports!.Single(r => r.TargetId == courseId);
        await admin.PostAsync($"/api/admin/reports/{report.Id}/resolve", null);
        var afterResolve = await admin.GetFromJsonAsync<List<ReportDto>>("/api/admin/reports", ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Forbidden, byStudent.StatusCode);
        Assert.DoesNotContain(afterResolve!, r => r.Id == report.Id);
    }
}
