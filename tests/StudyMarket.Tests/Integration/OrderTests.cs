using System.Net;
using System.Net.Http.Json;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Orders;
using static StudyMarket.Tests.Integration.TestData;

namespace StudyMarket.Tests.Integration;

public class OrderTests(ApiFactory api)
{
    [Fact]
    public async Task Course_cannot_be_bought_twice_or_by_its_author()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (courseId, _) = await CreateCourseAsync(creator);
        var student = await api.CreateClientAsync(Role.Student);

        await OrderAsync(student, courseId);
        var second = await student.PostAsJsonAsync("/api/orders", new CreateOrderRequest(courseId));
        var byAuthor = await creator.PostAsJsonAsync("/api/orders", new CreateOrderRequest(courseId));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, byAuthor.StatusCode);
    }
}
