using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="ErrorSanitizer.Detail"/> — the SR-14 choke point that keeps a leaked ERP
/// connection-string password out of exception messages returned to callers.
/// </summary>
public sealed class ErrorSanitizerTests
{
    [Theory]
    [InlineData("Host=erp;Port=5432;Password=hunter2;Database=db", "Host=erp;Port=5432;Password=***;Database=db")]
    [InlineData("connection failed: pwd=s3cr3t;Host=erp", "connection failed: pwd=***;Host=erp")]
    [InlineData("PASSWORD = hunter2 ;Host=erp", "PASSWORD=***;Host=erp")]
    public void Detail_CredentialInMessage_IsRedacted(string message, string expected)
    {
        var ex = new InvalidOperationException(message);

        Assert.Equal(expected, ErrorSanitizer.Detail(ex));
    }

    [Fact]
    public void Detail_NoCredentialInMessage_IsUnchanged()
    {
        var ex = new InvalidOperationException("connection refused: host unreachable");

        Assert.Equal("connection refused: host unreachable", ErrorSanitizer.Detail(ex));
    }

    [Fact]
    public void Detail_MultipleCredentialsInMessage_RedactsEachOne()
    {
        var ex = new InvalidOperationException("Password=first;Host=erp;pwd=second;Database=db");

        Assert.Equal("Password=***;Host=erp;pwd=***;Database=db", ErrorSanitizer.Detail(ex));
    }

    [Fact]
    public void Detail_PasswordIsLastSegmentWithNoTrailingSemicolon_StillRedacted()
    {
        var ex = new InvalidOperationException("Host=erp;Password=hunter2");

        Assert.Equal("Host=erp;Password=***", ErrorSanitizer.Detail(ex));
    }

    [Fact]
    public void Scrub_BasicAuthCredential_IsRedacted() =>
        Assert.Equal("Authorization: Basic ***", ErrorSanitizer.Scrub("Authorization: Basic c3ZjOnNlY3JldA=="));

    [Fact]
    public void ForLogging_CleanException_IsReturnedAsIs()
    {
        var ex = new InvalidOperationException("connection refused");

        Assert.Same(ex, ErrorSanitizer.ForLogging(ex));
    }

    [Fact]
    public void ForLogging_CredentialInInnerException_IsScrubbedFromTheFullText()
    {
        var ex = new InvalidOperationException("outer", new ArgumentException("Host=erp;Password=hunter2"));

        var logged = ErrorSanitizer.ForLogging(ex).ToString();

        Assert.DoesNotContain("hunter2", logged);
        Assert.Contains("ArgumentException", logged);
    }
}
