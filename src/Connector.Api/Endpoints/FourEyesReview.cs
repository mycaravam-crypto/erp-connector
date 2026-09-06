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
    /// </summary>
    public static string? ValidateApprover(
        string operatorName,
        string? approver,
        IReadOnlyDictionary<string, string> userStore
    )
    {
        if (string.IsNullOrWhiteSpace(approver))
            return "Approver is required.";

        if (string.Equals(operatorName, approver, StringComparison.OrdinalIgnoreCase))
            return "Operator and approver must be different users (four-eyes principle).";

        if (!userStore.ContainsKey(approver))
            return $"Unknown approver: '{approver}'. Only registered users can approve a release.";

        return null;
    }
}
