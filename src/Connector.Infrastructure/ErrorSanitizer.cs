using System.Text.RegularExpressions;

namespace Connector.Infrastructure;

/// <summary>
/// Security-review finding SR-14: several endpoints intentionally return an exception's message to the
/// caller (a "test connection"/preview action is only useful if it says why it failed), but Npgsql can echo
/// the offending connection string — including the ERP password — back inside an exception message (e.g. on
/// a malformed connection string, see SR-02). <see cref="Detail"/> is the single choke point every such
/// call site routes through, so the diagnostic value survives but a credential can't leak through it.
/// </summary>
public static partial class ErrorSanitizer
{
    [GeneratedRegex(@"\b(password|pwd)\s*=\s*[^;]*", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialPattern();

    public static string Detail(Exception ex) => CredentialPattern().Replace(ex.Message, "$1=***");
}
