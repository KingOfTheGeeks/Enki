using System.Globalization;
using System.Text;

namespace SDI.Enki.Shared.Audit;

/// <summary>
/// Tiny CSV serializer for audit-feed exports. Hand-rolled rather
/// than pulling in CsvHelper because the surface here is one-shot
/// (admin downloads a .csv) and the escaping rules are simple
/// enough — RFC 4180 says wrap any field containing comma /
/// newline / quote in quotes, and double any embedded quotes.
///
/// <para>
/// Lives in Shared (not WebApi) so both the master-side audit
/// controllers and the Identity-host audit/auth-events controllers
/// produce CSV the same way without duplicating the writer.
/// </para>
///
/// <para>
/// Caller passes the column projections as <c>(name, accessor)</c>
/// pairs; the writer handles header row + escaping. Renders dates
/// in ISO 8601 round-trippable format (<c>"O"</c>) so spreadsheets
/// can sort them lexically without ambiguity. Null values render
/// as empty strings (not the literal <c>null</c>).
/// </para>
/// </summary>
public static class AuditCsv
{
    public static string Serialize<T>(
        IEnumerable<T> rows,
        IReadOnlyList<(string Name, Func<T, object?> Accessor)> columns)
    {
        var sb = new StringBuilder();

        // Header row
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(',');
            AppendField(sb, columns[i].Name);
        }
        sb.Append('\n');

        // Data rows
        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendField(sb, FormatValue(columns[i].Accessor(row)));
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string FormatValue(object? v) => v switch
    {
        null                  => "",
        DateTimeOffset dto    => dto.ToString("O", CultureInfo.InvariantCulture),
        DateTime dt           => dt.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f        => f.ToString(null, CultureInfo.InvariantCulture),
        _                     => v.ToString() ?? "",
    };

    private static void AppendField(StringBuilder sb, string value)
    {
        // Quote if the field contains comma, quote, CR, or LF — RFC 4180.
        // Embedded quotes get doubled. No quoting needed for the common case.
        var needsQuote = value.AsSpan().IndexOfAny(",\"\n\r") >= 0;

        if (!needsQuote)
        {
            sb.Append(value);
            return;
        }

        sb.Append('"');
        foreach (var c in value)
        {
            if (c == '"') sb.Append('"'); // double the quote
            sb.Append(c);
        }
        sb.Append('"');
    }
}
