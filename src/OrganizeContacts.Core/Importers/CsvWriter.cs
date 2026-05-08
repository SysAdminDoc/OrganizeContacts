using System.Text;

namespace OrganizeContacts.Core.Importers;

/// <summary>RFC 4180-style CSV writer. Quotes fields that contain commas, quotes, or newlines.</summary>
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
        if (f.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return f;
        return "\"" + f.Replace("\"", "\"\"") + "\"";
    }
}
