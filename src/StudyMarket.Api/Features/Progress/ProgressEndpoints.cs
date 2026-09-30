using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Courses;

namespace StudyMarket.Api.Features.Progress;

public record QuizSubmission(Dictionary<int, int> Answers); // id pytania -> indeks wybranej odpowiedzi

public record QuizResult(int Correct, int Total, int ScorePercent);

public record LessonProgressDto(int LessonId, bool Completed, int? BestQuizScore);

public record CourseProgress(int CourseId, int CompletedLessons, int TotalLessons, List<LessonProgressDto> Lessons)
{
    public int Percent => TotalLessons == 0 ? 0 : CompletedLessons * 100 / TotalLessons;
}

public static class ProgressEndpoints
{
    public static void MapProgressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Progress").RequireAuthorization();
        group.MapPost("/lessons/{id:int}/complete", CompleteLesson);
        group.MapPost("/lessons/{id:int}/quiz", SubmitQuiz);
        group.MapGet("/courses/{id:int}/progress", GetCourseProgress);
    }

    static async Task<Results<NoContent, NotFound, ForbidHttpResult>> CompleteLesson(int id, AppDbContext db, ClaimsPrincipal user)
    {
        var lesson = await ContentEndpoints.LoadLesson(id, db);
        if (lesson is null) return TypedResults.NotFound();
        if (!await user.CanViewContentAsync(lesson.Module.Course, db)) return TypedResults.Forbid();

        var progress = await GetOrCreateProgress(user.GetUserId(), id, db);
        progress.CompletedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    static async Task<Results<Ok<QuizResult>, NotFound, ForbidHttpResult>> SubmitQuiz(
        int id, QuizSubmission submission, AppDbContext db, ClaimsPrincipal user)
    {
        var lesson = await ContentEndpoints.LoadLesson(id, db);
        if (lesson is null || lesson.QuizQuestions.Count == 0) return TypedResults.NotFound();
        if (!await user.CanViewContentAsync(lesson.Module.Course, db)) return TypedResults.Forbid();

        var correct = lesson.CountCorrectAnswers(submission.Answers);
        var result = new QuizResult(correct, lesson.QuizQuestions.Count, correct * 100 / lesson.QuizQuestions.Count);

        var progress = await GetOrCreateProgress(user.GetUserId(), id, db);
        progress.BestQuizScore = Math.Max(progress.BestQuizScore ?? 0, result.ScorePercent);
        await db.SaveChangesAsync();
        return TypedResults.Ok(result);
    }

    static async Task<Results<Ok<CourseProgress>, NotFound, ForbidHttpResult>> GetCourseProgress(
        int id, AppDbContext db, ClaimsPrincipal user)
    {
        var course = await db.Courses.FindAsync(id);
        if (course is null) return TypedResults.NotFound();
        if (!await user.CanViewContentAsync(course, db)) return TypedResults.Forbid();

        var userId = user.GetUserId();
        var lessonIds = await db.Lessons.Where(l => l.Module.CourseId == id)
            .OrderBy(l => l.Module.Position).ThenBy(l => l.Position).Select(l => l.Id).ToListAsync();
        var progress = await db.LessonProgress.Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId))
            .ToDictionaryAsync(p => p.LessonId);

        var lessons = lessonIds.Select(lessonId => progress.TryGetValue(lessonId, out var p)
            ? new LessonProgressDto(lessonId, p.CompletedAt is not null, p.BestQuizScore)
            : new LessonProgressDto(lessonId, false, null)).ToList();

        return TypedResults.Ok(new CourseProgress(id, lessons.Count(l => l.Completed), lessons.Count, lessons));
    }

    static async Task<LessonProgress> GetOrCreateProgress(int userId, int lessonId, AppDbContext db)
    {
        var progress = await db.LessonProgress.FindAsync(userId, lessonId);
        if (progress is null)
        {
            progress = new LessonProgress { UserId = userId, LessonId = lessonId };
            db.LessonProgress.Add(progress);
        }
        return progress;
    }
}
