using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Features.Auth;

// Wydawanie i weryfikacja tokenów JWT. Konfiguracja w sekcji "Jwt" (Key, Issuer).
public static class Jwt
{
    public static TokenValidationParameters ValidationParameters(IConfiguration config) => new()
    {
        ValidIssuer = config["Jwt:Issuer"],
        ValidAudience = config["Jwt:Issuer"],
        IssuerSigningKey = SigningKey(config),
        NameClaimType = JwtRegisteredClaimNames.Sub,
        RoleClaimType = "role",
    };

    public static string CreateToken(User user, IConfiguration config) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Issuer"],
            Expires = DateTime.UtcNow.AddHours(12),
            SigningCredentials = new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.DisplayName),
                new Claim("role", user.Role.ToString()),
            ]),
        });

    private static SymmetricSecurityKey SigningKey(IConfiguration config) =>
        new(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
}
