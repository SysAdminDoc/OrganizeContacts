using System.Text;

namespace OrganizeContacts.Core.Importers;

/// <summary>RFC 4180-style CSV writer. Quotes fields that contain commas, quotes, or newlines,
/// and defangs Excel/Sheets formula-injection vectors so a contact field starting with
/// <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, <c>\t</c>, or <c>\r</c> can't trigger code execution
/// when the export is opened in a spreadsheet (CWE-1236, OWASP "CSV Injection").</summary>
public static class CsvWriter
{
    public static string Format(IEnumerable<string> row)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var f in row)
        {
            if (!first) sb.Append(',');
            sb.Append(Escape(f));
            first = false;
        }
        return sb.ToString();
    }

    public static async Task WriteAsync(StreamWriter writer, IEnumerable<IEnumerable<string>> rows, CancellationToken ct = default)
    {
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(Format(row));
        }
    }

    public static string Escape(string? f)
    {
        if (string.IsNullOrEmpty(f)) return string.Empty;
        var sanitized = StripFormulaPrefix(f);
        if (sanitized.IndexOfAny(QuotingTriggers) < 0) return sanitized;
        return "\"" + sanitized.Replace("\"", "\"\"") + "\"";
    }

    private static readonly char[] QuotingTriggers = new[] { ',', '"', '\r', '\n' };

    /// <summary>Prefix a single-quote (Excel's literal-text marker) when the cell starts
    /// with a character that Excel/Sheets/Numbers would treat as a formula. The single
    /// quote is invisible in the spreadsheet but neutralises evaluation. Idempotent —
    /// re-importing through our own reader simply sees the leading quote as literal.</summary>
    private static string StripFormulaPrefix(string f)
    {
        if (f.Length == 0) return f;
        var first = f[0];
        if (first == '=' || first == '+' || first == '-' || first == '@' ||
            first == '\t' || first == '\r')
        {
            return "'" + f;
        }
        return f;
    }
}
