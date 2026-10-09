using Connector.Api.Authorization;
using Connector.Infrastructure;

namespace Connector.Api.Endpoints;

/// <summary>
/// The Operator/Approver-distinctness check shared by every release endpoint that commits a change under
/// four-eyes review — export release (<see cref="ExportEndpoints"/>) and the import commit path
/// (<see cref="ImportRunEndpoints"/>).
/// </summary>
internal static class FourEyesReview
{
    // Registered against AddRateLimiter in Program.cs — per-client-IP fixed window on the release endpoints
    // that call ValidateApprover (ExportEndpoints, ImportRunEndpoints), so a brute-force attempt against an
    // approver's password gets throttled the same way /api/auth/login already is.
    internal const string ApprovalRateLimiterPolicyName = "four-eyes-approval";

    /// <summary>
    /// Returns a client-facing error message if <paramref name="approver"/> fails the four-eyes check against
    /// <paramref name="operatorName"/>, or <c>null</c> if it passes. <paramref name="approverPassword"/> is verified
    /// against the approver's own stored hash — without this, the operator's session alone could "approve" a
    /// release by typing any other registered username, which defeats the point of a dual-control check.
    /// Requiring their password here is a lightweight stand-in for a real second login. The approver must also
    /// hold <paramref name="releasePermission"/> themselves — the operator's own is checked by the endpoint's policy.
    /// </summary>
    public static async Task<string?> ValidateApproverAsync(
        string operatorName,
        string? approver,
        string? approverPassword,
        string releasePermission,
        ExportLogDbContext db,
        RolePermissionStore permissions
    )
    {
        if (string.IsNullOrWhiteSpace(approver))
            return "Approver is required.";

        if (string.Equals(operatorName, approver, StringComparison.OrdinalIgnoreCase))
            return "Operator and approver must be different users (four-eyes principle).";

        var approverUser = await db.FindUserAsync(approver);
        if (approverUser is null)
            return $"Unknown approver: '{approver}'. Only registered users can approve a release.";

        if (
            string.IsNullOrEmpty(approverPassword)
            || !BCrypt.Net.BCrypt.Verify(approverPassword, approverUser.PasswordHash)
        )
            return "Approver password is missing or incorrect.";

        if (!await permissions.HasAsync(approverUser.Role, releasePermission))
            return $"'{approverUser.Username}' is not allowed to approve this release.";

        return null;
    }
}
