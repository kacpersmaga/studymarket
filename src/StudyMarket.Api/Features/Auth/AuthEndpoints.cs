using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Features.Auth;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    [Required, MaxLength(100)] string DisplayName,
    [AllowedValues(Role.Student, Role.Creator)] Role Role); // roli Admin nie można nadać sobie samemu

public record AuthResponse(string Token);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapPost("/register", Register);
    }

    static async Task<Results<Ok<AuthResponse>, Conflict, ProblemHttpResult>> Register(
        RegisterRequest request, AppDbContext db, IPasswordHasher<User> hasher, IConfiguration config)
    {
        var email = request.Email.ToLowerInvariant();
        var user = new User
        {
            Email = email, DisplayName = request.DisplayName, Role = request.Role,
        };

        if (await db.Users.AnyAsync(u => u.Email == email))
            return TypedResults.Conflict();

        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return TypedResults.Ok(new AuthResponse(Jwt.CreateToken(user, config)));
    }
}
