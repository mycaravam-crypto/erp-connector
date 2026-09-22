namespace Connector.Core.DataSources;

/// <summary>
/// Result of <see cref="IDataSourceProvider.TestConnectionAsync"/>: either the live <see cref="Schema"/> on
/// success, or a sanitized, user-facing <see cref="Error"/> on failure — never both. A provider must never
/// let a raw exception (which can echo connection-string/credential detail) reach <see cref="Error"/>
/// unsanitized.
/// </summary>
public record TestConnectionResult(bool Success, SourceSchema? Schema, string? Error)
{
    public static TestConnectionResult Ok(SourceSchema schema) => new(true, schema, null);

    public static TestConnectionResult Failed(string error) => new(false, null, error);
}
