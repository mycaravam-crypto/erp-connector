namespace Connector.Api;

/// <summary>
/// Provides hard-coded dev users so Development mode works out of the box without
/// pre-computing BCrypt hashes. Do not call in Production.
/// </summary>
internal static class DevAuthSeed
{
    /// <summary>alice / alice123 and bob / bob123 with BCrypt work-factor 4 (fast for dev).</summary>
    internal static Dictionary<string, string> CreateUsers() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["alice"] = BCrypt.Net.BCrypt.HashPassword("alice123", workFactor: 4),
            ["bob"] = BCrypt.Net.BCrypt.HashPassword("bob123", workFactor: 4),
        };
}
