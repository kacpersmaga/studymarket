using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Features.Courses;

public record CourseRequest(
    [Required, MaxLength(200)] string Title,
    [MaxLength(5000)] string? Description,
    [Required, MaxLength(100)] string Subject,
    SchoolLevel Level,
    [Range(1, 8)] int? Grade,
    [Range(2, 10_000)] decimal Price); // 2 zł to minimalna kwota płatności u operatora (Stripe, PLN)

public record CourseSummary(
    int Id, string Title, string Subject, SchoolLevel Level, int? Grade, decimal Price, string AuthorName, bool IsPublished);

// CanEdit i HasAccess mówią frontendowi, czy pokazać przycisk "Edytuj", "Ucz się" czy "Kup".
public record CourseDetails(
    int Id, string Title, string Description, string Subject, SchoolLevel Level, int? Grade, decimal Price,
    string AuthorName, bool IsPublished, bool CanEdit, bool HasAccess,
    List<ModuleOutline> Modules);

public record ModuleOutline(int Id, string Title, int Position, List<LessonOutline> Lessons);

public record LessonOutline(int Id, string Title, int Position);

public static class CourseEndpoints
{
    public static void MapCourseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/courses").WithTags("Courses");
        group.MapGet("/", GetCatalog);
        group.MapGet("/{id:int}", GetCourse);
        group.MapGet("/mine", GetMyCourses).RequireAuthorization();
        group.MapPost("/", CreateCourse).RequireAuthorization(p => p.RequireRole(nameof(Role.Creator), nameof(Role.Admin)));
        group.MapPut("/{id:int}", UpdateCourse).RequireAuthorization();
        group.MapDelete("/{id:int}", DeleteCourse).RequireAuthorization();
        group.MapPost("/{id:int}/publish", (int id, AppDbContext db, ClaimsPrincipal user) => SetPublished(id, true, db, user)).RequireAuthorization();
        group.MapPost("/{id:int}/unpublish", (int id, AppDbContext db, ClaimsPrincipal user) => SetPublished(id, false, db, user)).RequireAuthorization();
    }

    // Katalog: tylko opublikowane kursy.
    static async Task<Ok<List<CourseSummary>>> GetCatalog(
        AppDbContext db)
    {
        var query = db.Courses.Where(c => c.IsPublished);

        return TypedResults.Ok(await query.OrderByDescending(c => c.CreatedAt).Select(ToSummary).ToListAsync());
    }

    // Kursy, których jestem autorem (także nieopublikowane).
    static async Task<Ok<List<CourseSummary>>> GetMyCourses(AppDbContext db, ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        return TypedResults.Ok(await db.Courses.Where(c => c.AuthorId == userId).Select(ToSummary).ToListAsync());
    }

    // Szczegóły i spis treści (bez treści lekcji) - widoczne dla wszystkich, jeśli kurs jest opublikowany.
    static async Task<Results<Ok<CourseDetails>, NotFound>> GetCourse(int id, AppDbContext db, ClaimsPrincipal user)
    {
        var course = await db.Courses
            .Include(c => c.Author)
            .Include(c => c.Modules).ThenInclude(m => m.Lessons)
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.Id == id);
        if (course is null || (!course.IsPublished && !user.CanEdit(course)))
            return TypedResults.NotFound();

        var canEdit = user.CanEdit(course);
        var hasAccess = canEdit || (user.Identity?.IsAuthenticated == true && await user.HasPurchasedAsync(id, db));

        var modules = course.Modules.OrderBy(m => m.Position).Select(m => new ModuleOutline(m.Id, m.Title, m.Position,
            m.Lessons.OrderBy(l => l.Position).Select(l => new LessonOutline(l.Id, l.Title, l.Position)).ToList())).ToList();

        return TypedResults.Ok(new CourseDetails(course.Id, course.Title, course.Description, course.Subject, course.Level,
            course.Grade, course.Price, course.Author.DisplayName, course.IsPublished,
            canEdit, hasAccess, modules));
    }

    static async Task<Created<CourseSummary>> CreateCourse(CourseRequest request, AppDbContext db, ClaimsPrincipal user)
    {
        var course = new Course { AuthorId = user.GetUserId(), Title = request.Title, Subject = request.Subject };
        Apply(request, course);
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        var summary = await db.Courses.Where(c => c.Id == course.Id).Select(ToSummary).SingleAsync();
        return TypedResults.Created($"/api/courses/{course.Id}", summary);
    }

    static async Task<Results<NoContent, NotFound, ForbidHttpResult>> UpdateCourse(
        int id, CourseRequest request, AppDbContext db, ClaimsPrincipal user)
    {
        var course = await db.Courses.FindAsync(id);
        if (course is null) return TypedResults.NotFound();
        if (!user.CanEdit(course)) return TypedResults.Forbid();

        Apply(request, course);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    static async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> DeleteCourse(
        int id, AppDbContext db, ClaimsPrincipal user)
    {
        var course = await db.Courses.FindAsync(id);
        if (course is null) return TypedResults.NotFound();
        if (!user.CanEdit(course)) return TypedResults.Forbid();

        db.Courses.Remove(course);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    static async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> SetPublished(
        int id, bool publish, AppDbContext db, ClaimsPrincipal user)
    {
        var course = await db.Courses.FindAsync(id);
        if (course is null) return TypedResults.NotFound();
        if (!user.CanEdit(course)) return TypedResults.Forbid();

        course.IsPublished = publish;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    static void Apply(CourseRequest request, Course course)
    {
        course.Title = request.Title;
        course.Description = request.Description ?? "";
        course.Subject = request.Subject;
        course.Level = request.Level;
        course.Grade = request.Grade;
        course.Price = request.Price;
    }

    static readonly System.Linq.Expressions.Expression<Func<Course, CourseSummary>> ToSummary = c =>
        new CourseSummary(c.Id, c.Title, c.Subject, c.Level, c.Grade, c.Price, c.Author.DisplayName, c.IsPublished);
}
