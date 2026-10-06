using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Courses;
using static StudyMarket.Tests.Integration.TestData;

namespace StudyMarket.Tests.Integration;

public class CommunityTests(ApiFactory api)
{
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
}
