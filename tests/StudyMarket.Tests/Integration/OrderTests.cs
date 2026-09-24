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

    [Fact]
    public async Task Webhook_with_forged_signature_is_rejected()
    {
        var (courseId, _) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var student = await api.CreateClientAsync(Role.Student);
        var order = await OrderAsync(student, courseId);

        var response = await api.SendStripeEventAsync(order.Id, secret: "whsec_atakujacy");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var orders = await student.GetFromJsonAsync<List<OrderDto>>("/api/orders", ApiFactory.Json);
        Assert.Equal(OrderStatus.Pending, orders!.Single().Status);
    }

    [Fact]
    public async Task Repeated_webhook_is_processed_once()
    {
        var (courseId, _) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var student = await api.CreateClientAsync(Role.Student);
        var orderId = await PurchaseAsync(api, student, courseId);
        var paidAt = (await student.GetFromJsonAsync<List<OrderDto>>("/api/orders", ApiFactory.Json))!.Single().PaidAt;

        (await api.SendStripeEventAsync(orderId)).EnsureSuccessStatusCode();

        var orders = await student.GetFromJsonAsync<List<OrderDto>>("/api/orders", ApiFactory.Json);
        Assert.Equal(paidAt, orders!.Single().PaidAt);
    }

    // BLIK i Przelewy24 mogą najpierw zakończyć sesję bez zapłaty, a potwierdzenie przysłać osobnym zdarzeniem.
    [Fact]
    public async Task Delayed_payment_is_confirmed_by_async_event()
    {
        var (courseId, _) = await CreateCourseAsync(await api.CreateClientAsync(Role.Creator));
        var student = await api.CreateClientAsync(Role.Student);
        var order = await OrderAsync(student, courseId);

        await api.SendStripeEventAsync(order.Id, paymentStatus: "unpaid");
        var afterCompleted = (await student.GetFromJsonAsync<List<OrderDto>>("/api/orders", ApiFactory.Json))!.Single().Status;
        await api.SendStripeEventAsync(order.Id, type: "checkout.session.async_payment_succeeded");
        var afterAsync = (await student.GetFromJsonAsync<List<OrderDto>>("/api/orders", ApiFactory.Json))!.Single().Status;

        Assert.Equal(OrderStatus.Pending, afterCompleted);
        Assert.Equal(OrderStatus.Paid, afterAsync);
    }

    [Fact]
    public async Task Creator_earnings_include_platform_commission()
    {
        var creator = await api.CreateClientAsync(Role.Creator);
        var (courseId, _) = await CreateCourseAsync(creator); // 49,99 zł
        await PurchaseAsync(api, await api.CreateClientAsync(Role.Student), courseId);
        await PurchaseAsync(api, await api.CreateClientAsync(Role.Student), courseId);
        await OrderAsync(await api.CreateClientAsync(Role.Student), courseId); // nieopłacone - nie liczy się

        var earnings = await creator.GetFromJsonAsync<List<CourseEarnings>>("/api/creator/earnings");

        // prowizja 15%: 49,99 * 0,15 = 7,50 zł od sztuki
        Assert.Equal(new CourseEarnings(courseId, SampleCourse.Title, 2, 99.98m, 15.00m, 84.98m), earnings!.Single());
    }
}
