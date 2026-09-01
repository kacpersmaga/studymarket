using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Features;

// Wszystkie reguły "kto może co" w jednym miejscu.
public static class Access
{
    public static int GetUserId(this ClaimsPrincipal user) => int.Parse(user.Identity!.Name!);

    // Kurs edytuje jego autor albo administrator.
    public static bool CanEdit(this ClaimsPrincipal user, Course course) =>
        user.IsInRole(nameof(Role.Admin)) || user.Identity?.Name == course.AuthorId.ToString();

    // Kupujący = ma opłacone zamówienie na kurs.
    public static Task<bool> HasPurchasedAsync(this ClaimsPrincipal user, int courseId, AppDbContext db) =>
        db.Orders.AnyAsync(o => o.CourseId == courseId && o.UserId == user.GetUserId() && o.Status == OrderStatus.Paid);

    // Treść lekcji (i komentarze pod nią) widzi edytujący albo kupujący.
    public static async Task<bool> CanViewContentAsync(this ClaimsPrincipal user, Course course, AppDbContext db) =>
        user.CanEdit(course) || await user.HasPurchasedAsync(course.Id, db);
}
