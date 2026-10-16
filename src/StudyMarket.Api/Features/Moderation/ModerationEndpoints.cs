using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Features.Moderation;

public record ReportRequest(ReportTarget TargetType, int TargetId, [Required, MaxLength(1000)] string Reason);

public record ReportDto(int Id, ReportTarget TargetType, int TargetId, string Reason, DateTime CreatedAt);

// Notice and action (DSA): każdy zalogowany zgłasza treść, administrator przegląda zgłoszenia
// i reaguje istniejącymi operacjami (wycofanie kursu ze sprzedaży, usunięcie komentarza).
public static class ModerationEndpoints
{
    public static void MapModerationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/reports", CreateReport).WithTags("Moderation").RequireAuthorization();

        var admin = app.MapGroup("/api/admin/reports").WithTags("Moderation")
            .RequireAuthorization(p => p.RequireRole(nameof(Role.Admin)));
        admin.MapGet("/", GetOpenReports);
        admin.MapPost("/{id:int}/resolve", Resolve);
    }

    static async Task<Results<Created, NotFound>> CreateReport(ReportRequest request, AppDbContext db, ClaimsPrincipal user)
    {
        var exists = request.TargetType == ReportTarget.Course
            ? await db.Courses.AnyAsync(c => c.Id == request.TargetId)
            : await db.Comments.AnyAsync(c => c.Id == request.TargetId);
        if (!exists) return TypedResults.NotFound();

        db.Reports.Add(new Report
        {
            ReporterId = user.GetUserId(), TargetType = request.TargetType, TargetId = request.TargetId, Reason = request.Reason,
        });
        await db.SaveChangesAsync();
        return TypedResults.Created();
    }

    static async Task<Ok<List<ReportDto>>> GetOpenReports(AppDbContext db) =>
        TypedResults.Ok(await db.Reports.Where(r => r.ResolvedAt == null).OrderBy(r => r.CreatedAt)
            .Select(r => new ReportDto(r.Id, r.TargetType, r.TargetId, r.Reason, r.CreatedAt)).ToListAsync());

    static async Task<Results<NoContent, NotFound>> Resolve(int id, AppDbContext db)
    {
        var report = await db.Reports.FindAsync(id);
        if (report is null) return TypedResults.NotFound();

        report.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
