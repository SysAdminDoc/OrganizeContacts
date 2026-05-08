using System.Text;

namespace OrganizeContacts.Core.Importers;

/// <summary>
/// Minimal RFC 4180 CSV reader. Handles quoted fields with escaped double-quotes
/// and embedded commas/newlines. No external dependency required for our needs.
/// </summary>
public static class CsvReader
{
    public static IEnumerable<List<string>> Read(StreamReader reader)
    {
        var sb = new StringBuilder();
        var fields = new List<string>();
        var inQuotes = false;
        var lineHadContent = false;

        int Peek() => reader.Peek();
        int Read() => reader.Read();

        int ch;
        while ((ch = Read()) != -1)
        {
            var c = (char)ch;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (Peek() == '"')
                    {
                        sb.Append('"');
                        Read();
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    lineHadContent = true;
                    break;
                case ',':
                    fields.Add(sb.ToString());
                    sb.Clear();
                    lineHadContent = true;
                    break;
                case '\r':
                    if (Peek() == '\n') Read();
                    fields.Add(sb.ToString());
                    sb.Clear();
                    if (lineHadContent || fields.Count > 1) yield return fields;
                    fields = new List<string>();
                    lineHadContent = false;
                    break;
                case '\n':
                    fields.Add(sb.ToString());
                    sb.Clear();
                    if (lineHadContent || fields.Count > 1) yield return fields;
                    fields = new List<string>();
                    lineHadContent = false;
                    break;
                default:
                    sb.Append(c);
                    lineHadContent = true;
                    break;
            }
        }

        if (lineHadContent || sb.Length > 0)
        {
            fields.Add(sb.ToString());
            yield return fields;
        }
    }
}
