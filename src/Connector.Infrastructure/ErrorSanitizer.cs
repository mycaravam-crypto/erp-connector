using System.Text.RegularExpressions;

namespace Connector.Infrastructure;

/// <summary>
/// Strips credentials from error text. Several endpoints return an exception's message to the caller (a
/// "test connection"/preview action is only useful if it says why it failed), but Npgsql can echo the
/// offending connection string — including the ERP password — back inside an exception message.
/// <see cref="Detail"/> is the single choke point every such call site routes through, so the diagnostic
/// value survives but a credential can't leak through it. <see cref="ForLogging"/> does the same for logged
/// exceptions, and <see cref="Scrub"/> for any other text such as audit details.
/// </summary>
public static partial class ErrorSanitizer
{
    // password=…/pwd=… up to the next ';' (connection strings), and an HTTP Basic credential (ServiceNow).
    [GeneratedRegex(@"\b(password|pwd)\s*=\s*[^;]*", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialPattern();

    [GeneratedRegex(@"\b(Basic)\s+[A-Za-z0-9+/]{8,}={0,2}", RegexOptions.IgnoreCase)]
    private static partial Regex BasicAuthPattern();

    public static string Scrub(string text) =>
        BasicAuthPattern().Replace(CredentialPattern().Replace(text, "$1=***"), "$1 ***");

    public static string Detail(Exception ex) => Scrub(ex.Message);

    /// <summary>
    /// <paramref name="ex"/> itself when its full text (type, message, stack trace, inner exceptions) holds no
    /// credential; otherwise a plain <see cref="Exception"/> carrying that text scrubbed — log sinks render an
    /// exception through <see cref="Exception.ToString"/>, so the log line keeps every diagnostic detail except
    /// the secret. Applied to every log event by the API's log formatter.
    /// </summary>
    public static Exception ForLogging(Exception ex)
    {
        var text = ex.ToString();
        var scrubbed = Scrub(text);
        return scrubbed == text ? ex : new Exception(scrubbed);
    }
}
