namespace Connector.Api.Endpoints;

/// <summary>
/// The Operator/Approver-distinctness check shared by every release endpoint that commits a change under
/// four-eyes review — export release (<see cref="ExportEndpoints"/>) and, per import-definitions.md §5, the
/// import commit path (<see cref="ImportRunEndpoints"/>). Generalized out of the export release handler,
/// where it used to live inline, rather than duplicated for the import side (import-definitions.md §5: "The
/// four-eyes Operator/Approver-distinctness check currently inline in the export release endpoint —
/// generalized into a shared helper both directions call, rather than duplicated").
/// </summary>
internal static class FourEyesReview
{
    /// <summary>
    /// Returns a client-facing error message if <paramref name="approver"/> fails the four-eyes check against
    /// <paramref name="operatorName"/> and <paramref name="userStore"/>, or <c>null</c> if it passes.
    /// <paramref name="approverPassword"/> is verified against the approver's own stored hash — without this,
    /// the operator's session alone could "approve" a release by typing any other registered username, which
    /// defeats the point of a dual-control check (security audit finding: the approver never actually
    /// authenticated). Requiring their password here is a lightweight stand-in for a real second login.
    /// </summary>
    public static string? ValidateApprover(
        string operatorName,
        string? approver,
        string? approverPassword,
        IReadOnlyDictionary<string, string> userStore
    )
    {
        if (string.IsNullOrWhiteSpace(approver))
            return "Approver is required.";

        if (string.Equals(operatorName, approver, StringComparison.OrdinalIgnoreCase))
            return "Operator and approver must be different users (four-eyes principle).";

        if (!userStore.TryGetValue(approver, out var approverPasswordHash))
            return $"Unknown approver: '{approver}'. Only registered users can approve a release.";

        if (
            string.IsNullOrEmpty(approverPassword)
            || !BCrypt.Net.BCrypt.Verify(approverPassword, approverPasswordHash)
        )
            return "Approver password is missing or incorrect.";

        return null;
    }
}
