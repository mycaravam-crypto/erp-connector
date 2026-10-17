namespace Connector.Infrastructure;

/// <summary>
/// Structural checks on an <see cref="Connector.Core.DynamicExport.ExportNode.Filter"/> WHERE fragment, shared by
/// save-time validation (ExportDefinitionEndpoints) and every run (DynamicExportService.BuildExportNodePlan), so a
/// definition saved before a field was denylisted is caught too. Complements — does not replace — the endpoint's
/// character-set and keyword screening.
/// </summary>
public static class ExportFilterGuard
{
    /// <summary>
    /// True when every '(' outside a string literal has a matching ')' and every literal is closed. The engine wraps
    /// a filter in parentheses and ANDs it with its own conditions (join-key match, record selection); an unbalanced
    /// filter such as <c>1=1) OR (1=1</c> would escape that wrapper and drop those conditions.
    /// </summary>
    public static bool IsBalanced(string filter) => Scan(filter).Balanced;

    /// <summary>
    /// The first identifier outside a string literal that is on <paramref name="denylist"/>, or null. A filter
    /// can't return a denied column, but it can still test one (<c>technician_name LIKE 'A%'</c>) and leak it
    /// through which rows come back.
    /// </summary>
    public static string? FindDeniedIdentifier(string filter, IReadOnlySet<string> denylist) =>
        Scan(filter).Identifiers.FirstOrDefault(denylist.Contains);

    // One pass over the filter: identifiers outside '...' literals ('' is an escaped quote), and whether parentheses
    // and quotes are balanced.
    private static (List<string> Identifiers, bool Balanced) Scan(string filter)
    {
        var identifiers = new List<string>();
        var depth = 0;
        var balanced = true;
        var i = 0;
        while (i < filter.Length)
        {
            var c = filter[i];
            if (c == '\'')
            {
                i = SkipStringLiteral(filter, i + 1);
                if (i < 0)
                    return (identifiers, false);
            }
            else if (IsIdentifierStart(c))
            {
                var start = i;
                while (i < filter.Length && IsIdentifierPart(filter[i]))
                    i++;
                identifiers.Add(filter[start..i]);
            }
            else
            {
                if (c == '(')
                    depth++;
                else if (c == ')' && --depth < 0)
                    balanced = false;
                i++;
            }
        }
        return (identifiers, balanced && depth == 0);
    }

    // Index just past the literal's closing quote, or -1 when it never closes.
    private static int SkipStringLiteral(string filter, int i)
    {
        while (i < filter.Length)
        {
            if (filter[i] != '\'')
                i++;
            else if (i + 1 < filter.Length && filter[i + 1] == '\'')
                i += 2;
            else
                return i + 1;
        }
        return -1;
    }

    private static bool IsIdentifierStart(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || c is >= '0' and <= '9';
}
