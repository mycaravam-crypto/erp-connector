using System.Security.Cryptography;
using System.Text;

namespace Connector.Infrastructure;

/// <summary>
/// Machine-to-machine credential check for endpoints a "dedicated API user" calls directly (no interactive
/// login) — e.g. an external system triggering a saved export preset. Deliberately not BCrypt: a BCrypt
/// verify is intentionally slow (work-factor tuned to resist brute-forcing a human-chosen password) and
/// this runs on every single request rather than once at login, while a generated API key is already
/// high-entropy, so a fast, constant-time SHA-256 comparison is the appropriate trade-off here.
/// Never stores or compares the raw key — only its hash, both at rest (config) and in memory.
/// </summary>
public sealed class ApiKeyStore(IReadOnlyDictionary<string, string> nameByKeyHash)
{
    /// <summary>Hex-encoded SHA-256 of <paramref name="rawKey"/> — what belongs in <c>Auth:ApiKeys[].KeyHash</c>
    /// config (generate with e.g. <c>printf '%s' '&lt;key&gt;' | sha256sum</c>).</summary>
    public static string Hash(string rawKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();

    /// <summary>Checks <paramref name="rawKey"/> against every configured key hash in fixed time per
    /// comparison (so a single guess can't be timed against one stored hash), and on a match returns the
    /// friendly <c>Name</c> to use as the request's identity (audit log, <c>ClaimTypes.Name</c>).</summary>
    public bool TryAuthenticate(string rawKey, out string name)
    {
        var candidate = Encoding.UTF8.GetBytes(Hash(rawKey));
        foreach (var (storedHash, storedName) in nameByKeyHash)
        {
            if (CryptographicOperations.FixedTimeEquals(candidate, Encoding.UTF8.GetBytes(storedHash)))
            {
                name = storedName;
                return true;
            }
        }
        name = "";
        return false;
    }
}
