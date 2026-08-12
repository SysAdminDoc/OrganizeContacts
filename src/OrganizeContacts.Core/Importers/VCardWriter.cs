using System.Text;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Photos;

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

        if (!string.IsNullOrWhiteSpace(c.Uid)) AppendLine(sb, $"UID:{EscapeUri(c.Uid!)}");

        // N (structured) and FN
        var n = string.Join(';', new[]
        {
            c.FamilyName ?? string.Empty,
            c.GivenName ?? string.Empty,
            c.AdditionalNames ?? string.Empty,
            c.HonorificPrefix ?? string.Empty,
            c.HonorificSuffix ?? string.Empty,
        }.Select(EscapeStructured));
        AppendLine(sb, $"N:{n}");

        var fn = string.IsNullOrWhiteSpace(c.FormattedName) ? c.DisplayName : c.FormattedName!;
        AppendLine(sb, $"FN:{Escape(fn)}");

        if (!string.IsNullOrWhiteSpace(c.Nickname)) AppendLine(sb, $"NICKNAME:{Escape(c.Nickname!)}");
        if (!string.IsNullOrWhiteSpace(c.Organization)) AppendLine(sb, $"ORG:{EscapeStructured(c.Organization!)}");
        if (!string.IsNullOrWhiteSpace(c.Title)) AppendLine(sb, $"TITLE:{Escape(c.Title!)}");
        if (c.Birthday.HasValue) AppendLine(sb, $"BDAY:{c.Birthday.Value:yyyy-MM-dd}");
        if (c.Anniversary.HasValue)
        {
            var property = Version == VCardVersion.V4_0 ? "ANNIVERSARY" : "X-ANNIVERSARY";
            AppendLine(sb, $"{property}:{c.Anniversary.Value:yyyy-MM-dd}");
        }
        if (!string.IsNullOrWhiteSpace(c.Notes)) AppendLine(sb, $"NOTE:{Escape(c.Notes!)}");

        foreach (var p in c.Phones)
        {
            var paramStr = BuildTypeParams(PhoneType(p.Kind), p.IsPreferred);
            var v = string.IsNullOrEmpty(p.E164) ? p.Raw : p.E164!;
            if (Version == VCardVersion.V4_0)
                AppendLine(sb, $"TEL;VALUE=uri{paramStr}:{ToTelephoneUri(v)}");
            else
                AppendLine(sb, $"TEL{paramStr}:{Escape(v)}");
        }

        foreach (var e in c.Emails)
        {
            var paramStr = BuildTypeParams(EmailType(e.Kind), e.IsPreferred);
            AppendLine(sb, $"EMAIL{paramStr}:{Escape(e.Address)}");
        }

        foreach (var a in c.Addresses)
        {
            var paramStr = BuildTypeParams(AddressType(a.Kind), a.IsPreferred);
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

        foreach (var u in c.Urls) AppendLine(sb, $"URL:{EscapeUri(u)}");

        if (c.Categories.Count > 0)
            AppendLine(sb, $"CATEGORIES:{string.Join(',', c.Categories.Select(Escape))}");

        if (c.PhotoBytes is { Length: > 0 })
        {
            var b64 = Convert.ToBase64String(c.PhotoBytes);
            var mime = PhotoSanitizer.NormalizeImageMimeType(c.PhotoMimeType) ??
                       PhotoSanitizer.InferMimeType(c.PhotoBytes) ??
                       "image/jpeg";
            if (Version == VCardVersion.V4_0)
            {
                AppendLine(sb, $"PHOTO:data:{mime};base64,{b64}");
            }
            else
            {
                var typ = mime["image/".Length..].ToUpperInvariant();
                AppendLine(sb, $"PHOTO;ENCODING=b;TYPE={typ}:{b64}");
            }
        }

        if (c.CustomFields.TryGetValue(VCardImporter.PreservedPhotoUriField, out var photoUri) &&
            Uri.TryCreate(photoUri, UriKind.Absolute, out _))
        {
            var valueParam = Version == VCardVersion.V4_0 ? string.Empty : ";VALUE=URI";
            AppendLine(sb, $"PHOTO{valueParam}:{EscapeUri(photoUri)}");
        }

        foreach (var kv in c.CustomFields)
        {
            if (kv.Key.Equals(VCardImporter.PreservedPhotoUriField, StringComparison.OrdinalIgnoreCase)) continue;
            var key = NormalizeCustomPropertyName(kv.Key);
            AppendLine(sb, $"{key}:{Escape(kv.Value)}");
        }

        var rev = c.Rev ?? c.UpdatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        AppendLine(sb, $"REV:{Escape(rev)}");
        AppendLine(sb, "END:VCARD");
    }

    private string BuildTypeParams(string? type, bool pref)
    {
        if (Version == VCardVersion.V3_0)
        {
            var values = new List<string>();
            if (!string.IsNullOrEmpty(type)) values.Add(type);
            if (pref) values.Add("PREF");
            return values.Count == 0 ? string.Empty : ";TYPE=" + string.Join(',', values);
        }

        var bits = new List<string>();
        if (!string.IsNullOrEmpty(type))
            bits.Add($"TYPE={type}");
        if (pref) bits.Add("PREF=1");
        return bits.Count == 0 ? string.Empty : ";" + string.Join(';', bits);
    }

    private static string? PhoneType(PhoneKind kind) => kind switch
    {
        PhoneKind.Mobile => "CELL",
        PhoneKind.Home => "HOME",
        PhoneKind.Work => "WORK",
        PhoneKind.Fax => "FAX",
        PhoneKind.Pager => "PAGER",
        PhoneKind.Main => "VOICE",
        _ => null,
    };

    private static string? EmailType(EmailKind kind) => kind switch
    {
        EmailKind.Personal => "HOME",
        EmailKind.Work => "WORK",
        _ => null,
    };

    private static string? AddressType(AddressKind kind) => kind switch
    {
        AddressKind.Home => "HOME",
        AddressKind.Work => "WORK",
        _ => null,
    };

    private static string ToTelephoneUri(string value)
    {
        var number = value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ? value[4..] : value;
        var encoded = Uri.EscapeDataString(number)
            .Replace("%2B", "+", StringComparison.OrdinalIgnoreCase)
            .Replace("%28", "(", StringComparison.OrdinalIgnoreCase)
            .Replace("%29", ")", StringComparison.OrdinalIgnoreCase)
            .Replace("%2A", "*", StringComparison.OrdinalIgnoreCase)
            .Replace("%23", "#", StringComparison.OrdinalIgnoreCase)
            .Replace("%3B", ";", StringComparison.OrdinalIgnoreCase)
            .Replace("%3D", "=", StringComparison.OrdinalIgnoreCase)
            .Replace("%2C", ",", StringComparison.OrdinalIgnoreCase);
        return "tel:" + encoded;
    }

    private static string EscapeUri(string value) => value
        .Replace("\r", "%0D", StringComparison.Ordinal)
        .Replace("\n", "%0A", StringComparison.Ordinal)
        .Replace("\t", "%09", StringComparison.Ordinal)
        .Replace(" ", "%20", StringComparison.Ordinal);

    private static string NormalizeCustomPropertyName(string key)
    {
        var source = key.StartsWith("X-", StringComparison.OrdinalIgnoreCase) ? key : "X-" + key;
        var normalized = new StringBuilder(source.Length);
        foreach (var ch in source)
            normalized.Append(char.IsAsciiLetterOrDigit(ch) || ch == '-' ? char.ToUpperInvariant(ch) : '-');
        return normalized.Length > 2 ? normalized.ToString() : "X-FIELD";
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
        for (int i = start; i < s.Length;)
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
                cp < 0x80 ? 1 :
                cp < 0x800 ? 2 :
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
