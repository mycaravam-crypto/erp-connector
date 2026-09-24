using Connector.Infrastructure;
using Serilog.Events;
using Serilog.Formatting;

namespace Connector.Api;

/// <summary>
/// The last stop before a log line is written. Wraps the real formatter and hands it a copy of
/// each event with credentials scrubbed — from the exception (<see cref="ErrorSanitizer.ForLogging"/>; Npgsql can
/// echo a connection string, password included, in an exception message) and from every string property — and any
/// destructured property named <c>Password</c> dropped. One choke point for every logger in the process, so no
/// call site has to remember it.
/// </summary>
public sealed class SanitizingLogFormatter(ITextFormatter inner) : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output) =>
        inner.Format(
            new LogEvent(
                logEvent.Timestamp,
                logEvent.Level,
                logEvent.Exception is null ? null : ErrorSanitizer.ForLogging(logEvent.Exception),
                logEvent.MessageTemplate,
                logEvent.Properties.Select(p => new LogEventProperty(p.Key, Sanitize(p.Value)))
            ),
            output
        );

    private static LogEventPropertyValue Sanitize(LogEventPropertyValue value) =>
        value switch
        {
            ScalarValue { Value: string s } => new ScalarValue(ErrorSanitizer.Scrub(s)),
            StructureValue structure => new StructureValue(
                structure
                    .Properties.Where(p => !p.Name.Equals("Password", StringComparison.OrdinalIgnoreCase))
                    .Select(p => new LogEventProperty(p.Name, Sanitize(p.Value))),
                structure.TypeTag
            ),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(Sanitize)),
            DictionaryValue dictionary => new DictionaryValue(
                dictionary.Elements.Select(kv => KeyValuePair.Create(kv.Key, Sanitize(kv.Value)))
            ),
            _ => value,
        };
}
