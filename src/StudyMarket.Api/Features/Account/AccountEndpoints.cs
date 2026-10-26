using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Orders;

namespace StudyMarket.Api.Features.Account;

public record ProfileDto(int Id, string Email, string DisplayName, Role Role, DateOnly BirthDate, DateTime TermsAcceptedAt);

// Wszystkie dane osobowe użytkownika w formacie nadającym się do odczytu maszynowego (JSON).
public record DataExport(
    ProfileDto Profile, List<OrderDto> Orders, List<ExportedComment> Comments, List<ExportedReview> Reviews,
    List<ExportedProgress> Progress);

public record ExportedComment(int LessonId, string Text, DateTime CreatedAt);
public record ExportedReview(int CourseId, int Rating, string? Text, DateTime CreatedAt);
public record ExportedProgress(int LessonId, DateTime? CompletedAt, int? BestQuizScore);

public record ChangePasswordRequest([Required] string CurrentPassword, [Required, MinLength(8)] string NewPassword);

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").WithTags("Account (RODO)").RequireAuthorization();
        group.MapGet("/", GetProfile);
        group.MapPut("/password", ChangePassword).RequireRateLimiting("auth"); // zgadywanie obecnego hasła jak przy logowaniu
        group.MapGet("/export", Export);
        group.MapDelete("/", DeleteAccount);
    }

    static async Task<Ok<ProfileDto>> GetProfile(AppDbContext db, ClaimsPrincipal user) =>
        TypedResults.Ok(ToProfile(await db.Users.SingleAsync(u => u.Id == user.GetUserId())));

    // Wymaga obecnego hasła - przejęty na chwilę token (np. niezablokowany komputer) nie wystarczy do przejęcia konta.
    static async Task<Results<NoContent, ProblemHttpResult>> ChangePassword(
        ChangePasswordRequest request, AppDbContext db, ClaimsPrincipal user, IPasswordHasher<User> hasher)
    {
        var me = await db.Users.SingleAsync(u => u.Id == user.GetUserId());
        if (hasher.VerifyHashedPassword(me, me.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return TypedResults.Problem("Obecne hasło jest nieprawidłowe.", statusCode: 400);

        me.PasswordHash = hasher.HashPassword(me, request.NewPassword);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // RODO art. 15 (prawo dostępu) i art. 20 (przenoszenie danych).
    static async Task<Ok<DataExport>> Export(AppDbContext db, ClaimsPrincipal user)
    {
        var id = user.GetUserId();
        return TypedResults.Ok(new DataExport(
            ToProfile(await db.Users.SingleAsync(u => u.Id == id)),
            await db.Orders.Where(o => o.UserId == id)
                .Select(o => new OrderDto(o.Id, o.CourseId, o.Course.Title, o.Price, o.Status, o.CreatedAt, o.PaidAt)).ToListAsync(),
            await db.Comments.Where(c => c.UserId == id).Select(c => new ExportedComment(c.LessonId, c.Text, c.CreatedAt)).ToListAsync(),
            await db.Reviews.Where(r => r.UserId == id).Select(r => new ExportedReview(r.CourseId, r.Rating, r.Text, r.CreatedAt)).ToListAsync(),
            await db.LessonProgress.Where(p => p.UserId == id)
                .Select(p => new ExportedProgress(p.LessonId, p.CompletedAt, p.BestQuizScore)).ToListAsync()));
    }

    // RODO art. 17 (prawo do usunięcia danych). Zamówień nie usuwamy - przepisy podatkowe i rachunkowe
    // wymagają przechowywania dowodów sprzedaży przez 5 lat (art. 6 ust. 1 lit. c RODO). Dlatego konto jest
    // anonimizowane: znikają dane identyfikujące osobę, a zamówienia zostają powiązane z anonimowym rekordem.
    static async Task<NoContent> DeleteAccount(AppDbContext db, ClaimsPrincipal user)
    {
        var id = user.GetUserId();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await db.Comments.Where(c => c.UserId == id).ExecuteDeleteAsync();
        await db.Reviews.Where(r => r.UserId == id).ExecuteDeleteAsync();
        await db.LessonProgress.Where(p => p.UserId == id).ExecuteDeleteAsync();
        await db.Reports.Where(r => r.ReporterId == id).ExecuteDeleteAsync();
        await db.Orders.Where(o => o.UserId == id && o.Status == OrderStatus.Pending).ExecuteDeleteAsync();
        // Kursy twórcy znikają z katalogu, ale osoby, które je kupiły, zachowują dostęp.
        await db.Courses.Where(c => c.AuthorId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsPublished, false));
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.Email, $"deleted-{id}@studymarket.invalid")
            .SetProperty(u => u.DisplayName, "Usunięty użytkownik")
            .SetProperty(u => u.PasswordHash, "")
            .SetProperty(u => u.BirthDate, DateOnly.MinValue)
            .SetProperty(u => u.IsDeleted, true));

        await transaction.CommitAsync();
        return TypedResults.NoContent();
    }

    static ProfileDto ToProfile(User u) => new(u.Id, u.Email, u.DisplayName, u.Role, u.BirthDate, u.TermsAcceptedAt);
}
