using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Connector.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Connector.Api.Endpoints;

static class AuthEndpoints
{
    internal static void MapAuthEndpoints(this WebApplication app, IReadOnlyDictionary<string, string> userStore)
    {
        app.MapPost(
            "/api/auth/login",
            async (LoginRequest req, AuditService audit) =>
            {
                if (
                    string.IsNullOrWhiteSpace(req.Username)
                    || !userStore.TryGetValue(req.Username, out var hash)
                    || !BCrypt.Net.BCrypt.Verify(req.Password ?? "", hash)
                )
                    return Results.Unauthorized();

                var expiry = app.Configuration.GetValue<int>("Auth:JwtExpiryHours", defaultValue: 8);
                var jwtSecret =
                    app.Configuration["Auth:JwtSecret"]
                    ?? throw new InvalidOperationException("Auth:JwtSecret is not configured.");
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                var token = new JwtSecurityToken(
                    claims: [new Claim(ClaimTypes.Name, req.Username)],
                    expires: DateTime.UtcNow.AddHours(expiry),
                    signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
                );
                await audit.LogAsync(req.Username, "login");
                return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), req.Username));
            }
        );

        // Dev-only: returns a BCrypt hash for a plaintext password (to seed appsettings for production users).
        if (app.Environment.IsDevelopment())
        {
            app.MapPost(
                "/api/auth/hash",
                (HashRequest req) =>
                    Results.Ok(new { Hash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 11) })
            );
        }
    }
}
