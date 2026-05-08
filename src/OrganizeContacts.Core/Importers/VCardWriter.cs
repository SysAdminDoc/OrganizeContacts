using System.Text;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

public enum VCardVersion
{
    V3_0,
    V4_0,
}

/// <summary>vCard writer for 3.0 (default) or 4.0. Preserves UID, REV, CustomFields (X-*).</summary>
public sealed class VCardWriter
{
    public VCardVersion Version { get; init; } = VCardVersion.V3_0;

    public string Write(Contact c) => WriteAll(new[] { c });

    public string WriteAll(IEnumerable<Contact> contacts)
    {
        var sb = new StringBuilder();
        foreach (var c in contacts) WriteCard(sb, c);
        return sb.ToString();
    }

    public async Task WriteFileAsync(string path, IEnumerable<Contact> contacts, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var w = new StreamWriter(fs, new UTF8Encoding(false));
        foreach (var c in contacts)
        {
            ct.ThrowIfCancellationRequested();
            var sb = new StringBuilder();
            WriteCard(sb, c);
            await w.WriteAsync(sb.ToString());
        }
    }

    private void WriteCard(StringBuilder sb, Contact c)
    {
        var version = Version == VCardVersion.V4_0 ? "4.0" : "3.0";
        AppendLine(sb, "BEGIN:VCARD");
        AppendLine(sb, $"VERSION:{version}");

        if (!string.IsNullOrWhiteSpace(c.Uid)) AppendLine(sb, $"UID:{Escape(c.Uid!)}");

        // N (structured) and FN
        var n = string.Join(';', new[]
        {
            c.FamilyName ?? string.Empty,
            c.GivenName ?? string.Empty,
            c.AdditionalNames ?? string.Empty,
            c.HonorificPrefix ?? string.Empty,
            c.HonorificSuffix ?? string.Empty,
        }.Select(EscapeStructured));
        if (n != ";;;;") AppendLine(sb, $"N:{n}");

        var fn = string.IsNullOrWhiteSpace(c.FormattedName) ? c.DisplayName : c.FormattedName!;
        if (!string.IsNullOrWhiteSpace(fn)) AppendLine(sb, $"FN:{Escape(fn)}");

        if (!string.IsNullOrWhiteSpace(c.Nickname)) AppendLine(sb, $"NICKNAME:{Escape(c.Nickname!)}");
        if (!string.IsNullOrWhiteSpace(c.Organization)) AppendLine(sb, $"ORG:{EscapeStructured(c.Organization!)}");
        if (!string.IsNullOrWhiteSpace(c.Title)) AppendLine(sb, $"TITLE:{Escape(c.Title!)}");
        if (c.Birthday.HasValue) AppendLine(sb, $"BDAY:{c.Birthday.Value:yyyy-MM-dd}");
        if (c.Anniversary.HasValue) AppendLine(sb, $"ANNIVERSARY:{c.Anniversary.Value:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(c.Notes)) AppendLine(sb, $"NOTE:{Escape(c.Notes!)}");

        foreach (var p in c.Phones)
        {
            var paramStr = BuildTypeParams(p.Kind.ToString().ToUpperInvariant(), p.IsPreferred);
            var v = string.IsNullOrEmpty(p.E164) ? p.Raw : p.E164!;
            AppendLine(sb, $"TEL{paramStr}:{Escape(v)}");
        }

        foreach (var e in c.Emails)
        {
            var paramStr = BuildTypeParams(e.Kind.ToString().ToUpperInvariant(), e.IsPreferred);
            AppendLine(sb, $"EMAIL{paramStr}:{Escape(e.Address)}");
        }

        foreach (var a in c.Addresses)
        {
            var paramStr = BuildTypeParams(a.Kind.ToString().ToUpperInvariant(), false);
            var adr = string.Join(';', new[]
            {
                a.PoBox ?? string.Empty,
                a.Extended ?? string.Empty,
                a.Street ?? string.Empty,
                a.Locality ?? string.Empty,
                a.Region ?? string.Empty,
                a.PostalCode ?? string.Empty,
                a.Country ?? string.Empty,
            }.Select(EscapeStructured));
            AppendLine(sb, $"ADR{paramStr}:{adr}");
        }

        foreach (var u in c.Urls) AppendLine(sb, $"URL:{Escape(u)}");

        if (c.Categories.Count > 0)
            AppendLine(sb, $"CATEGORIES:{string.Join(',', c.Categories.Select(Escape))}");

        if (c.PhotoBytes is { Length: > 0 })
        {
            var b64 = Convert.ToBase64String(c.PhotoBytes);
            if (Version == VCardVersion.V4_0)
            {
                var mime = string.IsNullOrEmpty(c.PhotoMimeType) ? "image/jpeg" : c.PhotoMimeType!;
                AppendLine(sb, $"PHOTO:data:{mime};base64,{b64}");
            }
            else
            {
                var typ = (c.PhotoMimeType ?? "image/jpeg").Replace("image/", "").ToUpperInvariant();
                AppendLine(sb, $"PHOTO;ENCODING=b;TYPE={typ}:{b64}");
            }
        }

        foreach (var kv in c.CustomFields)
        {
            var key = kv.Key.StartsWith("X-") ? kv.Key : "X-" + kv.Key;
            AppendLine(sb, $"{key}:{Escape(kv.Value)}");
        }

        var rev = c.Rev ?? c.UpdatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        AppendLine(sb, $"REV:{rev}");
        AppendLine(sb, "END:VCARD");
    }

    private static string BuildTypeParams(string type, bool pref)
    {
        var bits = new List<string>();
        if (!string.IsNullOrEmpty(type) && !type.Equals("OTHER", StringComparison.OrdinalIgnoreCase))
            bits.Add($"TYPE={type}");
        if (pref) bits.Add("TYPE=PREF");
        return bits.Count == 0 ? string.Empty : ";" + string.Join(';', bits);
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case ',': sb.Append("\\,"); break;
                case ';': sb.Append("\\;"); break;
                case '\r': break;
                case '\n': sb.Append("\\n"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static string EscapeStructured(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case ',': sb.Append("\\,"); break;
                case ';': sb.Append("\\;"); break;
                case '\r': break;
                case '\n': sb.Append("\\n"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// RFC 6350 §3.2 line folding at 75 octets (NOT chars). Continuations are prefixed
    /// with a single space, and we only break on UTF-16 code-unit boundaries that round-trip
    /// through UTF-8 — never inside a surrogate pair, never mid-codepoint.
    /// </summary>
    private static void AppendLine(StringBuilder sb, string line)
    {
        const int firstLineLimit = 75;
        const int contLimit = 74; // 1 leading space + 74 = 75 octets
        if (Encoding.UTF8.GetByteCount(line) <= firstLineLimit)
        {
            sb.Append(line).Append("\r\n");
            return;
        }

        var first = true;
        int i = 0;
        while (i < line.Length)
        {
            var limit = first ? firstLineLimit : contLimit;
            var take = OctetsThatFit(line, i, limit);
            if (take == 0) break; // single character exceeds limit; emit what we have
            if (!first) sb.Append(' ');
            sb.Append(line, i, take).Append("\r\n");
            i += take;
            first = false;
        }
    }

    /// <summary>Returns the largest character count from <paramref name="start"/> that
    /// encodes to no more than <paramref name="maxOctets"/> bytes in UTF-8 and does not
    /// split a surrogate pair.</summary>
    private static int OctetsThatFit(string s, int start, int maxOctets)
    {
        var bytes = 0;
        var taken = 0;
        for (int i = start; i < s.Length; )
        {
            int cp;
            int codeUnits;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                codeUnits = 2;
            }
            else
            {
                cp = s[i];
                codeUnits = 1;
            }

            int cpBytes =
                cp < 0x80    ? 1 :
                cp < 0x800   ? 2 :
                cp < 0x10000 ? 3 : 4;

            if (bytes + cpBytes > maxOctets) break;
            bytes += cpBytes;
            taken += codeUnits;
            i += codeUnits;
        }
        // Guarantee progress when even one codepoint exceeds the limit.
        if (taken == 0 && start < s.Length)
        {
            taken = char.IsHighSurrogate(s[start]) && start + 1 < s.Length ? 2 : 1;
        }
        return taken;
    }
}
