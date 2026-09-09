using Connector.Api;
using Connector.Api.Endpoints;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="FourEyesReview.ValidateApprover"/> (security audit finding: the approver was
/// previously just a free-text username picked by the operator, never independently authenticated — any
/// single account could "approve" its own release by typing a colleague's name). Verifies the approver's
/// own password is now required and checked against their real hash.
/// </summary>
public sealed class FourEyesReviewTests
{
    // alice/alice123, bob/bob123 — see DevAuthSeed's doc comment.
    private static readonly Dictionary<string, string> UserStore = DevAuthSeed.CreateUsers();

    [Fact]
    public void SameOperatorAndApprover_Rejected()
    {
        var error = FourEyesReview.ValidateApprover("alice", "alice", "alice123", UserStore);

        Assert.NotNull(error);
        Assert.Contains("different users", error);
    }

    [Fact]
    public void UnknownApprover_Rejected()
    {
        var error = FourEyesReview.ValidateApprover("alice", "carol", "whatever", UserStore);

        Assert.NotNull(error);
        Assert.Contains("Unknown approver", error);
    }

    [Fact]
    public void MissingApproverPassword_Rejected()
    {
        var error = FourEyesReview.ValidateApprover("alice", "bob", null, UserStore);

        Assert.NotNull(error);
        Assert.Contains("password", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WrongApproverPassword_Rejected()
    {
        var error = FourEyesReview.ValidateApprover("alice", "bob", "not-bobs-password", UserStore);

        Assert.NotNull(error);
        Assert.Contains("password", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OperatorCannotSelfApprove_ByGuessingOrKnowingAnotherPassword_StillRequiresThatPersonsOwnPassword()
    {
        // The operator (alice) cannot complete a release just by knowing bob's username — she'd need bob's
        // actual password too, which she authored this test knowing but wouldn't in reality.
        var error = FourEyesReview.ValidateApprover("alice", "bob", "alice123", UserStore);

        Assert.NotNull(error);
    }

    [Fact]
    public void CorrectApproverPassword_Accepted()
    {
        var error = FourEyesReview.ValidateApprover("alice", "bob", "bob123", UserStore);

        Assert.Null(error);
    }
}
