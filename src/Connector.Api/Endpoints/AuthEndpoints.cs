using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Connector.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Connector.Api.Endpoints;

static class AuthEndpoints
{
    // Registered against AddRateLimiter in Program.cs — per-client-IP fixed window, so a brute-force/
    // password-spray attempt against /api/auth/login gets throttled instead of running unbounded.
    internal const string LoginRateLimiterPolicyName = "login";

    // Not a real user's hash — verified against on every unknown-username login attempt so that path costs
    // the same BCrypt work as a known username with a wrong password. Without this, TryGetValue's near-instant
    // dictionary miss made response timing a reliable oracle for which usernames are registered.
    private const string DummyHashForTimingSafety = "$2b$11$EntBBq5Dhs85Yp/1C37FAOsBwtrLgKduXw4QIec8g189kye8C6eWm";

    internal static void MapAuthEndpoints(this WebApplication app, IReadOnlyDictionary<string, string> userStore)
    {
        app.MapPost(
                "/api/auth/login",
                async (LoginRequest req, AuditService audit) =>
                {
                    string? hash = null;
                    var known = !string.IsNullOrWhiteSpace(req.Username) && userStore.TryGetValue(req.Username, out hash);
                    var passwordOk = BCrypt.Net.BCrypt.Verify(req.Password ?? "", hash ?? DummyHashForTimingSafety);
                    if (!known || !passwordOk)
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
            )
            .RequireRateLimiting(LoginRateLimiterPolicyName);

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
