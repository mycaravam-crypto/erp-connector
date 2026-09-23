using Connector.Api;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Parsing;

namespace Connector.Integration.Tests;

/// <summary>Arbeitsauftrag 11: nothing the process logs may carry a credential — not an exception's text, not a
/// string property, not a destructured <c>Password</c>.</summary>
public sealed class SanitizingLogFormatterTests
{
    private static string Render(Exception? exception, params LogEventProperty[] properties)
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            exception,
            new MessageTemplateParser().Parse("Export failed {Detail}"),
            properties
        );
        using var output = new StringWriter();
        new SanitizingLogFormatter(new JsonFormatter()).Format(logEvent, output);
        return output.ToString();
    }

    [Fact]
    public void ExceptionText_IncludingInnerExceptions_IsScrubbed()
    {
        var ex = new InvalidOperationException(
            "Export failed",
            new ArgumentException("Host=erp;Password=hunter2;Database=db")
        );

        var line = Render(ex);

        Assert.DoesNotContain("hunter2", line);
        Assert.Contains("Password=***", line);
        Assert.Contains("InvalidOperationException", line); // diagnostics survive
    }

    [Fact]
    public void StringProperty_IsScrubbed() =>
        Assert.DoesNotContain(
            "hunter2",
            Render(null, new LogEventProperty("Detail", new ScalarValue("pwd=hunter2;Host=erp")))
        );

    [Fact]
    public void BasicAuthCredential_IsScrubbed() =>
        Assert.DoesNotContain(
            "c3ZjOnNlY3JldA==",
            Render(null, new LogEventProperty("Detail", new ScalarValue("Authorization: Basic c3ZjOnNlY3JldA==")))
        );

    [Fact]
    public void DestructuredPasswordProperty_IsDropped()
    {
        var config = new StructureValue(
            [
                new LogEventProperty("Host", new ScalarValue("erp")),
                new LogEventProperty("Password", new ScalarValue("hunter2")),
            ],
            "DataSourceConfig"
        );

        var line = Render(null, new LogEventProperty("Detail", config));

        Assert.DoesNotContain("hunter2", line);
        Assert.Contains("erp", line);
    }

    [Fact]
    public void JsonLine_StaysValidJson()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            Render(
                new InvalidOperationException("Password=x\"y;Host=erp"),
                new LogEventProperty("Detail", new ScalarValue("password=abc"))
            )
        );
        Assert.Equal("Error", doc.RootElement.GetProperty("Level").GetString());
    }
}
