using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Features.Orders;

public record CreateOrderRequest(int CourseId);

public record OrderDto(int Id, int CourseId, string CourseTitle, decimal Price, OrderStatus Status, DateTime CreatedAt, DateTime? PaidAt);

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders").RequireAuthorization();
        group.MapGet("/", GetMyOrders);
        group.MapPost("/", CreateOrder);
    }

    static async Task<Ok<List<OrderDto>>> GetMyOrders(AppDbContext db, ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        var orders = await db.Orders.Where(o => o.UserId == userId).OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderDto(o.Id, o.CourseId, o.Course.Title, o.Price, o.Status, o.CreatedAt, o.PaidAt))
            .ToListAsync();
        return TypedResults.Ok(orders);
    }

    // Tworzy zamówienie oczekujące na płatność. Płatność rozpoczyna się w PaymentEndpoints (checkout).
    static async Task<Results<Created<OrderDto>, NotFound, ProblemHttpResult>> CreateOrder(
        CreateOrderRequest request, AppDbContext db, ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        var course = await db.Courses.FindAsync(request.CourseId);
        if (course is null || !course.IsPublished) return TypedResults.NotFound();
        if (course.AuthorId == userId)
            return TypedResults.Problem("Nie można kupić własnego kursu.", statusCode: 409);
        if (await db.Orders.AnyAsync(o => o.UserId == userId && o.CourseId == course.Id))
            return TypedResults.Problem("Zamówienie na ten kurs już istnieje.", statusCode: 409);

        var order = new Order
        {
            UserId = userId, CourseId = course.Id, Price = course.Price, Status = OrderStatus.Pending,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/orders/{order.Id}",
            new OrderDto(order.Id, course.Id, course.Title, order.Price, order.Status, order.CreatedAt, order.PaidAt));
    }
}
