using Connector.Core.DynamicExport;
using Npgsql;

namespace Connector.Infrastructure;

/// <summary>
/// Query/build engine behind the connector's export paths: the legacy single-mapping flat/nested-JSON
/// pipeline (<see cref="ExportMappingConfig"/>, still served by <c>/api/pipeline/*</c> — see
/// <c>DynamicExportService.LegacyMapping.cs</c>) and the Phase 14 <see cref="ExportNode"/> tree engine
/// (Export Definitions 2.0 — see <c>DynamicExportService.ExportNode.cs</c>) side by side, plus the shared
/// GDPR denylist (<c>DynamicExportService.Gdpr.cs</c>) and output-format writers
/// (<c>DynamicExportService.Output.cs</c>) both paths use. Kept as one <c>partial</c> class split across
/// files by responsibility, rather than separate classes, so every existing
/// <c>DynamicExportService.*</c> call site across the API/Infrastructure/test projects keeps working
/// unchanged. This file holds the members shared across every other part: the nesting-depth cap, the
/// build-result shape, and the SQL-identifier/connection-string primitives both query engines build on.
/// </summary>
public static partial class DynamicExportService
{
    /// <summary>
    /// Recursion cap for JSON-only nested groups, enforced at save time (the primary, user-facing
    /// rejection point) and again defensively inside <see cref="BuildNestedGroupExpr"/> for any config
    /// that reaches query-build time without going through save-time validation. Far beyond any realistic
    /// use case (item → manufacturer → addresses is depth 2) — this exists solely to turn an unbounded
    /// recursive build into a catchable exception instead of an uncatchable <see cref="StackOverflowException"/>.
    /// </summary>
    public const int MaxNestedDepth = 16;

    /// <summary>
    /// Security-review finding SR-13: a real (non-preview) <see cref="ExportNode"/> run previously had no
    /// upper bound on rows at all — a pathological or accidentally-unfiltered definition could run
    /// unbounded. Generous on purpose (far beyond any current definition's real result size) so no existing
    /// export is affected; <see cref="ExecuteExportNodeQueryAsync"/> fails the run loudly if a query would
    /// exceed it, rather than silently truncating output a caller might mistake for a complete export.
    /// </summary>
    public const int MaxExportRowsPerRun = 500_000;

    public readonly record struct ExportBuildResult(byte[] Bytes, int RecordCount, string Extension);

    // Security-review finding SR-02: this previously interpolated Host/Database/Username/Password
    // straight into the connection-string text. A Password (or Username/Database) value containing
    // ";Host=evil;..." would append/override keys in the string Npgsql actually parses, letting a
    // caller redirect the connection despite ValidateHostAsync only checking the Host field.
    // NpgsqlConnectionStringBuilder sets each value as a typed property instead, so no field value can
    // ever be interpreted as connection-string syntax.
    public static string BuildConnectionString(ErpConnectionConfig cfg) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = cfg.Host,
            Port = cfg.Port,
            Database = cfg.Database,
            Username = cfg.Username,
            Password = cfg.Password,
            SslMode = SslMode.Prefer,
            TrustServerCertificate = true,
            Timeout = 5,
            CommandTimeout = 10,
        }.ConnectionString;

    /// <summary>
    /// Security-review finding SR-05: identifies *which system* a connection points at — host, port, and
    /// database — deliberately excluding Username/Password so a pure credential rotation against the same
    /// logical target (a password change, a different service account for the same database) never counts
    /// as a target change. Used to pin the connection an import run was staged against and verify it still
    /// matches at release (<see cref="ImportRunEntity.StagedConnectionFingerprint"/>,
    /// <see cref="ImportRunReleaser.ReleaseAsync"/>).
    /// </summary>
    public static string ConnectionFingerprint(ErpConnectionConfig cfg) => $"{cfg.Host}:{cfg.Port}/{cfg.Database}";

    // Safe SQL identifier quoting — wraps in double quotes and escapes embedded double quotes.
    public static string QI(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    // Single-quote escaping for a value embedded as a JSON key STRING LITERAL inside
    // json_build_object('key', expr, ...). Distinct from QI(), which double-quote-escapes SQL
    // IDENTIFIERS — reusing QI() here would be a correctness bug (Postgres would try to resolve
    // "key" as a column reference instead of treating it as a JSON object key). Shared by the legacy
    // nested-group builder and the ExportNode tree builder.
    private static string SqlLit(string value) => "'" + value.Replace("'", "''") + "'";
}
