using System.Security.Cryptography;
using System.Text;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

/// <summary>
/// Writes contact birthdays and anniversaries as private, yearly iCalendar events.
/// Contacts without either date are intentionally omitted.
/// </summary>
public sealed class IcsWriter
{
    private const int MaxLineOctets = 75;

    public string Write(Contact contact) => WriteAll(new[] { contact });

    public string WriteAll(IEnumerable<Contact> contacts)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "BEGIN:VCALENDAR");
        AppendLine(sb, "VERSION:2.0");
        AppendLine(sb, "PRODID:-//SysAdminDoc//OrganizeContacts//EN");
        AppendLine(sb, "CALSCALE:GREGORIAN");
        AppendLine(sb, "METHOD:PUBLISH");

        foreach (var contact in contacts)
        {
            if (contact.Birthday is { } birthday)
                WriteEvent(sb, contact, "birthday", "Birthday", birthday);
            if (contact.Anniversary is { } anniversary)
                WriteEvent(sb, contact, "anniversary", "Anniversary", anniversary);
        }

        AppendLine(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    public async Task WriteFileAsync(
        string path,
        IEnumerable<Contact> contacts,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await File.WriteAllTextAsync(path, WriteAll(contacts), new UTF8Encoding(false), ct);
    }

    private static void WriteEvent(
        StringBuilder sb,
        Contact contact,
        string eventKind,
        string eventLabel,
        DateOnly date)
    {
        var displayName = string.IsNullOrWhiteSpace(contact.DisplayName)
            ? "Unnamed contact"
            : contact.DisplayName;
        var uidSeed = !string.IsNullOrWhiteSpace(contact.Uid)
            ? contact.Uid!
            : contact.Id.ToString("N");
        var uidHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{uidSeed}|{eventKind}"))).ToLowerInvariant()[..24];
        var timestamp = contact.UpdatedAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");

        AppendLine(sb, "BEGIN:VEVENT");
        AppendLine(sb, $"UID:{uidHash}-{eventKind}@organizecontacts.local");
        AppendLine(sb, $"DTSTAMP:{timestamp}");
        AppendLine(sb, $"DTSTART;VALUE=DATE:{date:yyyyMMdd}");
        AppendLine(sb, "RRULE:FREQ=YEARLY");
        AppendLine(sb, $"SUMMARY:{EscapeText($"{displayName} — {eventLabel}")}");
        if (!string.IsNullOrWhiteSpace(contact.Notes))
            AppendLine(sb, $"DESCRIPTION:{EscapeText(contact.Notes!)}");
        AppendLine(sb, $"CATEGORIES:{eventLabel.ToUpperInvariant()}");
        AppendLine(sb, "CLASS:PRIVATE");
        AppendLine(sb, "END:VEVENT");
    }

    private static string EscapeText(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case ';': sb.Append("\\;"); break;
                case ',': sb.Append("\\,"); break;
                case '\r': break;
                case '\n': sb.Append("\\n"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Folds content lines at 75 UTF-8 octets, preserving whole code points.</summary>
    private static void AppendLine(StringBuilder sb, string line)
    {
        var remaining = line;
        var first = true;
        while (remaining.Length > 0)
        {
            var budget = first ? MaxLineOctets : MaxLineOctets - 1;
            var take = TakeUtf8Prefix(remaining, budget);
            if (take == 0) take = 1;

            if (!first) sb.Append(' ');
            sb.Append(remaining, 0, take);
            sb.Append("\r\n");
            remaining = remaining[take..];
            first = false;
        }
    }

    private static int TakeUtf8Prefix(string value, int maxOctets)
    {
        var octets = 0;
        var chars = 0;
        while (chars < value.Length)
        {
            var codePointLength = char.IsHighSurrogate(value[chars]) &&
                                  chars + 1 < value.Length &&
                                  char.IsLowSurrogate(value[chars + 1]) ? 2 : 1;
            var codePointOctets = Encoding.UTF8.GetByteCount(value.AsSpan(chars, codePointLength));
            if (octets + codePointOctets > maxOctets) break;
            octets += codePointOctets;
            chars += codePointLength;
        }
        return chars;
    }
}
