using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Features.Courses;

// Url - podpisany link ważny 1 godzinę; można go użyć bezpośrednio w <img src> lub <a href>.
public record AttachmentDto(int Id, string FileName, string ContentType, long Size, string Url);

public static class AttachmentEndpoints
{
    const long MaxFileSize = 20 * 1024 * 1024; // 20 MB - wystarcza na notatki i arkusze w PDF
    static readonly string[] AllowedTypes = ["application/pdf", "image/png", "image/jpeg"];
    static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(1);

    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Course content");
        // Ochrona CSRF zbędna: uwierzytelnianie tokenem w nagłówku, nie ciasteczkiem.
        group.MapPost("/lessons/{lessonId:int}/attachments", Upload).RequireAuthorization().DisableAntiforgery();
        group.MapGet("/attachments/{id:int}", Download); // token albo podpisany link
        group.MapDelete("/attachments/{id:int}", Delete).RequireAuthorization();
    }

    static async Task<Results<Created<AttachmentDto>, NotFound, ForbidHttpResult, ProblemHttpResult>> Upload(
        int lessonId, IFormFile file, AppDbContext db, ClaimsPrincipal user, IConfiguration config)
    {
        var lesson = await db.Lessons.Include(l => l.Module.Course).SingleOrDefaultAsync(l => l.Id == lessonId);
        if (lesson is null) return TypedResults.NotFound();
        if (!user.CanEdit(lesson.Module.Course)) return TypedResults.Forbid();
        if (file.Length is 0 or > MaxFileSize)
            return TypedResults.Problem("Plik jest pusty lub większy niż 20 MB.", statusCode: 400);
        if (!AllowedTypes.Contains(file.ContentType))
            return TypedResults.Problem("Dozwolone są tylko pliki PDF, PNG i JPG.", statusCode: 400);

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var attachment = new Attachment
        {
            LessonId = lessonId, FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, Data = stream.ToArray(),
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/attachments/{attachment.Id}",
            ToDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Data.Length, config));
    }

    // Plik nie ma publicznego adresu. Pobrać go można z tokenem (sprawdzany jest zakup) albo podpisanym linkiem,
    // który API wydaje tylko osobom z dostępem do lekcji.
    static async Task<Results<FileContentHttpResult, NotFound, UnauthorizedHttpResult, ForbidHttpResult>> Download(
        int id, long? expires, string? signature, AppDbContext db, ClaimsPrincipal user, IConfiguration config)
    {
        var attachment = await db.Attachments.Include(a => a.Lesson.Module.Course).SingleOrDefaultAsync(a => a.Id == id);
        if (attachment is null) return TypedResults.NotFound();

        var hasValidLink = expires is not null && signature is not null && IsValidSignature(id, expires.Value, signature, config);
        if (!hasValidLink)
        {
            if (user.Identity?.IsAuthenticated != true) return TypedResults.Unauthorized();
            if (!await user.CanViewContentAsync(attachment.Lesson.Module.Course, db)) return TypedResults.Forbid();
        }

        return TypedResults.File(attachment.Data, attachment.ContentType, attachment.FileName);
    }

    static async Task<Results<NoContent, NotFound, ForbidHttpResult>> Delete(int id, AppDbContext db, ClaimsPrincipal user)
    {
        var attachment = await db.Attachments.Include(a => a.Lesson.Module.Course).SingleOrDefaultAsync(a => a.Id == id);
        if (attachment is null) return TypedResults.NotFound();
        if (!user.CanEdit(attachment.Lesson.Module.Course)) return TypedResults.Forbid();

        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    public static AttachmentDto ToDto(int id, string fileName, string contentType, long size, IConfiguration config)
    {
        var expires = DateTimeOffset.UtcNow.Add(LinkLifetime).ToUnixTimeSeconds();
        return new AttachmentDto(id, fileName, contentType, size,
            $"/api/attachments/{id}?expires={expires}&signature={Sign(id, expires, config)}");
    }

    // Podpis HMAC-SHA256 z (id pliku, czas wygaśnięcia) - zmiana id lub przedłużenie ważności unieważnia podpis.
    static string Sign(int id, long expires, IConfiguration config) => Convert.ToHexStringLower(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(config["Jwt:Key"]!), Encoding.UTF8.GetBytes($"attachment:{id}:{expires}")));

    static bool IsValidSignature(int id, long expires, string signature, IConfiguration config) =>
        expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds() &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(id, expires, config)), Encoding.UTF8.GetBytes(signature));
}
