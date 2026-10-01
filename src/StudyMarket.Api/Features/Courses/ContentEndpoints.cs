using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Features.Courses;

public record ModuleRequest([Required, MaxLength(200)] string Title, int Position);

// Lekcja jest zapisywana w całości razem z fiszkami, quizem i zadaniami - jeden formularz po stronie frontendu.
public record LessonRequest(
    [Required, MaxLength(200)] string Title,
    string? Content,
    [Url] string? VideoUrl,
    int Position,
    List<FlashcardDto>? Flashcards,
    List<QuizQuestionRequest>? Quiz,
    List<ExerciseDto>? Exercises);

public record FlashcardDto([Required] string Front, [Required] string Back);

public record ExerciseDto([Required] string Text, [Required] string Solution);

public record QuizQuestionRequest([Required] string Text, [MinLength(2)] List<string> Options, int CorrectOption)
    : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (CorrectOption < 0 || CorrectOption >= Options.Count)
            yield return new ValidationResult("Indeks poprawnej odpowiedzi spoza listy opcji.", [nameof(CorrectOption)]);
    }
}

// CorrectOption jest widoczne tylko dla edytujących, żeby kupujący nie widzieli odpowiedzi.
public record QuizQuestionDto(int Id, string Text, List<string> Options, int? CorrectOption);

// Rozwiązania zadań są zwracane - służą do samosprawdzenia (frontend pokazuje je na żądanie).
public record LessonDetails(
    int Id, int CourseId, string Title, string Content, string? VideoUrl, int Position,
    List<FlashcardDto> Flashcards, List<QuizQuestionDto> Quiz, List<ExerciseDto> Exercises);

public record IdResponse(int Id);

public static class ContentEndpoints
{
    public static void MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Course content").RequireAuthorization();
        group.MapPost("/courses/{courseId:int}/modules", CreateModule);
        group.MapPut("/modules/{id:int}", UpdateModule);
        group.MapDelete("/modules/{id:int}", DeleteModule);
        group.MapPost("/modules/{moduleId:int}/lessons", CreateLesson);
        group.MapGet("/lessons/{id:int}", GetLesson);
        group.MapPut("/lessons/{id:int}", UpdateLesson);
        group.MapDelete("/lessons/{id:int}", DeleteLesson);
    }

    static async Task<Results<Created<IdResponse>, NotFound, ForbidHttpResult>> CreateModule(
        int courseId, ModuleRequest request, AppDbContext db, ClaimsPrincipal user)
    {
        var course = await db.Courses.FindAsync(courseId);
        if (course is null) return TypedResults.NotFound();
        if (!user.CanEdit(course)) return TypedResults.Forbid();

        var module = new Module { CourseId = courseId, Title = request.Title, Position = request.Position };
        db.Modules.Add(module);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/courses/{courseId}", new IdResponse(module.Id));
    }

    static async Task<Results<NoContent, NotFound, ForbidHttpResult>> UpdateModule(
        int id, ModuleRequest request, AppDbContext db, ClaimsPrincipal user)
    {
        var module = await db.Modules.Include(m => m.Course).SingleOrDefaultAsync(m => m.Id == id);
        if (module is null) return TypedResults.NotFound();
        if (!user.CanEdit(module.Course)) return TypedResults.Forbid();

        module.Title = request.Title;
        module.Position = request.Position;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteModule(int id, AppDbContext db, ClaimsPrincipal user)
    {
        var module = await db.Modules.Include(m => m.Course).SingleOrDefaultAsync(m => m.Id == id);
        if (module is null) return TypedResults.NotFound();
        if (!user.CanEdit(module.Course)) return TypedResults.Forbid();

        db.Modules.Remove(module);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    static async Task<Results<Created<IdResponse>, NotFound, ForbidHttpResult>> CreateLesson(
        int moduleId, LessonRequest request, AppDbContext db, ClaimsPrincipal user)
    {
        var module = await db.Modules.Include(m => m.Course).SingleOrDefaultAsync(m => m.Id == moduleId);
        if (module is null) return TypedResults.NotFound();
        if (!user.CanEdit(module.Course)) return TypedResults.Forbid();

        var lesson = new Lesson { ModuleId = moduleId, Title = request.Title };
        Apply(request, lesson);
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/lessons/{lesson.Id}", new IdResponse(lesson.Id));
    }

    static async Task<Results<Ok<LessonDetails>, NotFound, ForbidHttpResult>> GetLesson(
        int id, AppDbContext db, ClaimsPrincipal user)
    {
        var lesson = await LoadLesson(id, db);
        if (lesson is null) return TypedResults.NotFound();
        var course = lesson.Module.Course;
        if (!await user.CanViewContentAsync(course, db)) return TypedResults.Forbid();

        var canEdit = user.CanEdit(course);

        return TypedResults.Ok(new LessonDetails(lesson.Id, course.Id, lesson.Title, lesson.Content, lesson.VideoUrl, lesson.Position,
            lesson.Flashcards.Select(f => new FlashcardDto(f.Front, f.Back)).ToList(),
            lesson.QuizQuestions.OrderBy(q => q.Id)
                .Select(q => new QuizQuestionDto(q.Id, q.Text, q.Options, canEdit ? q.CorrectOption : null)).ToList(),
            lesson.Exercises.OrderBy(e => e.Id).Select(e => new ExerciseDto(e.Text, e.Solution)).ToList()));
    }

    static async Task<Results<NoContent, NotFound, ForbidHttpResult>> UpdateLesson(
        int id, LessonRequest request, AppDbContext db, ClaimsPrincipal user)
    {
        var lesson = await LoadLesson(id, db);
        if (lesson is null) return TypedResults.NotFound();
        if (!user.CanEdit(lesson.Module.Course)) return TypedResults.Forbid();

        Apply(request, lesson);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteLesson(int id, AppDbContext db, ClaimsPrincipal user)
    {
        var lesson = await LoadLesson(id, db);
        if (lesson is null) return TypedResults.NotFound();
        if (!user.CanEdit(lesson.Module.Course)) return TypedResults.Forbid();

        db.Lessons.Remove(lesson);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    public static Task<Lesson?> LoadLesson(int id, AppDbContext db) => db.Lessons
        .Include(l => l.Module).ThenInclude(m => m.Course)
        .Include(l => l.Flashcards)
        .Include(l => l.QuizQuestions)
        .Include(l => l.Exercises)
        .AsSplitQuery() // osobne zapytania zamiast jednego JOIN-a mnożącego wiersze
        .SingleOrDefaultAsync(l => l.Id == id);

    // Fiszki, pytania i zadania są zastępowane w całości - EF Core usuwa stare wiersze i dodaje nowe.
    static void Apply(LessonRequest request, Lesson lesson)
    {
        lesson.Title = request.Title;
        lesson.Content = request.Content ?? "";
        lesson.VideoUrl = request.VideoUrl;
        lesson.Position = request.Position;
        lesson.Exercises = (request.Exercises ?? []).Select(e => new Exercise { Text = e.Text, Solution = e.Solution }).ToList();
        lesson.Flashcards = (request.Flashcards ?? []).Select(f => new Flashcard { Front = f.Front, Back = f.Back }).ToList();
        lesson.QuizQuestions = (request.Quiz ?? [])
            .Select(q => new QuizQuestion { Text = q.Text, Options = q.Options, CorrectOption = q.CorrectOption }).ToList();
    }
}
