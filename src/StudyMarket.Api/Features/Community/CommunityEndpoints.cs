using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Features.Community;

public record CommentRequest([Required, MaxLength(2000)] string Text);

public record CommentDto(int Id, int UserId, string AuthorName, bool IsCourseAuthor, string Text, DateTime CreatedAt);

public record ReviewRequest([Range(1, 5)] int Rating, [MaxLength(2000)] string? Text);

public record ReviewDto(string AuthorName, int Rating, string? Text, DateTime CreatedAt);

// Komentarze i opinie mogą pisać tylko kupujący (i autor). Zakup jest naturalną barierą dla botów
// i fałszywych opinii - konto-bot musiałoby zapłacić za każdy kurs.
public static class CommunityEndpoints
{
    public static void MapCommunityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Comments & reviews");
        group.MapGet("/lessons/{lessonId:int}/comments", GetComments).RequireAuthorization();
        group.MapPost("/lessons/{lessonId:int}/comments", AddComment).RequireAuthorization();
        group.MapDelete("/comments/{id:int}", DeleteComment).RequireAuthorization();
        group.MapGet("/courses/{courseId:int}/reviews", GetReviews);
        group.MapPut("/courses/{courseId:int}/review", UpsertReview).RequireAuthorization();
    }

    static async Task<Results<Ok<List<CommentDto>>, NotFound, ForbidHttpResult>> GetComments(
        int lessonId, AppDbContext db, ClaimsPrincipal user)
    {
        var lesson = await db.Lessons.Include(l => l.Module.Course).SingleOrDefaultAsync(l => l.Id == lessonId);
        if (lesson is null) return TypedResults.NotFound();
        if (!await user.CanViewContentAsync(lesson.Module.Course, db)) return TypedResults.Forbid();

        var authorId = lesson.Module.Course.AuthorId;
        return TypedResults.Ok(await db.Comments.Where(c => c.LessonId == lessonId).OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(c.Id, c.UserId, c.User.DisplayName, c.UserId == authorId, c.Text, c.CreatedAt))
            .ToListAsync());
    }

    static async Task<Results<Created<CommentDto>, NotFound, ForbidHttpResult>> AddComment(
        int lessonId, CommentRequest request, AppDbContext db, ClaimsPrincipal user)
    {
        var lesson = await db.Lessons.Include(l => l.Module.Course).SingleOrDefaultAsync(l => l.Id == lessonId);
        if (lesson is null) return TypedResults.NotFound();
        if (!await user.CanViewContentAsync(lesson.Module.Course, db)) return TypedResults.Forbid();

        var comment = new Comment { LessonId = lessonId, UserId = user.GetUserId(), Text = request.Text };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/lessons/{lessonId}/comments", new CommentDto(comment.Id, comment.UserId,
            user.FindFirstValue("name")!, comment.UserId == lesson.Module.Course.AuthorId, comment.Text, comment.CreatedAt));
    }

    // Komentarz usuwa jego autor, autor kursu (moderacja własnego kursu) albo administrator.
    static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteComment(int id, AppDbContext db, ClaimsPrincipal user)
    {
        var comment = await db.Comments.Include(c => c.Lesson.Module.Course).SingleOrDefaultAsync(c => c.Id == id);
        if (comment is null) return TypedResults.NotFound();
        if (comment.UserId != user.GetUserId() && !user.CanEdit(comment.Lesson.Module.Course)) return TypedResults.Forbid();

        db.Comments.Remove(comment);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    static async Task<Ok<List<ReviewDto>>> GetReviews(int courseId, AppDbContext db) =>
        TypedResults.Ok(await db.Reviews.Where(r => r.CourseId == courseId).OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewDto(r.User.DisplayName, r.Rating, r.Text, r.CreatedAt)).ToListAsync());

    // Jedna opinia na kupującego - ponowne wysłanie ją nadpisuje.
    static async Task<Results<NoContent, NotFound, ForbidHttpResult>> UpsertReview(
        int courseId, ReviewRequest request, AppDbContext db, ClaimsPrincipal user)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == courseId)) return TypedResults.NotFound();
        if (!await user.HasPurchasedAsync(courseId, db)) return TypedResults.Forbid();

        var userId = user.GetUserId();
        var review = await db.Reviews.FindAsync(courseId, userId);
        if (review is null)
        {
            review = new Review { CourseId = courseId, UserId = userId };
            db.Reviews.Add(review);
        }
        review.Rating = request.Rating;
        review.Text = request.Text;
        review.CreatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
