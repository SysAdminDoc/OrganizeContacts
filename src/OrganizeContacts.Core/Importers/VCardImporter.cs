using System.Globalization;
using System.Text;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

/// <summary>
/// Standards-aware vCard reader supporting versions 2.1, 3.0, 4.0.
/// Handles line folding, RFC 6868 parameter escaping, quoted-printable + base64 + 8-bit encodings,
/// grouped properties (item1.TEL), embedded PHOTO data, X-* preservation, and UID/REV.
/// </summary>
public sealed class VCardImporter : IContactImporter
{
    public string Name => "vCard 2.1/3.0/4.0";
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".vcf", ".vcard" };

    public bool CanRead(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public async IAsyncEnumerable<Contact> ReadAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var raw = new List<string>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            raw.Add(line);
        }

        var unfolded = Unfold(raw);

        var inCard = false;
        var card = new List<string>();
        foreach (var line in unfolded)
        {
            if (line.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                inCard = true;
                card.Clear();
                continue;
            }
            if (line.StartsWith("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (inCard && card.Count > 0)
                {
                    var c = ParseCard(card, path);
                    if (c is not null) yield return c;
                }
                inCard = false;
                continue;
            }
            if (inCard) card.Add(line);
        }
    }

    /// <summary>RFC 6350 line unfolding: continuation if next line begins with WS.</summary>
    internal static List<string> Unfold(IReadOnlyList<string> raw)
    {
        var unfolded = new List<string>(raw.Count);
        foreach (var line in raw)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && unfolded.Count > 0)
                unfolded[^1] += line[1..];
            else
                unfolded.Add(line);
        }
        return unfolded;
    }

    private static Contact? ParseCard(IReadOnlyList<string> lines, string sourceFile)
    {
        // Detect VERSION first to drive encoding decisions
        var version = "3.0";
        foreach (var l in lines)
        {
            if (l.StartsWith("VERSION:", StringComparison.OrdinalIgnoreCase))
            {
                version = l[8..].Trim();
                break;
            }
        }

        var contact = new Contact
        {
            SourceFile = sourceFile,
            SourceFormat = $"vCard {version}",
        };
        var seenAny = false;

        foreach (var line in lines)
        {
            var prop = ParseProperty(line, version);
            if (prop is null) continue;

            switch (prop.Name)
            {
                case "VERSION":
                    break; // already captured

                case "UID":
                    contact.Uid = prop.Value;
                    break;

                case "REV":
                    contact.Rev = prop.Value;
                    break;

                case "FN":
                    contact.FormattedName = prop.Value;
                    seenAny = true;
                    break;

                case "N":
                {
                    var n = SplitStructured(prop.Value);
                    if (n.Length >= 1) contact.FamilyName = NullIfEmpty(n[0]);
                    if (n.Length >= 2) contact.GivenName = NullIfEmpty(n[1]);
                    if (n.Length >= 3) contact.AdditionalNames = NullIfEmpty(n[2]);
                    if (n.Length >= 4) contact.HonorificPrefix = NullIfEmpty(n[3]);
                    if (n.Length >= 5) contact.HonorificSuffix = NullIfEmpty(n[4]);
                    seenAny = true;
                    break;
                }

                case "NICKNAME":
                    contact.Nickname = prop.Value;
                    break;

                case "ORG":
                    contact.Organization = SplitStructured(prop.Value).FirstOrDefault();
                    break;

                case "TITLE":
                case "ROLE":
                    if (string.IsNullOrEmpty(contact.Title)) contact.Title = prop.Value;
                    break;

                case "BDAY":
                    contact.Birthday = ParseDate(prop.Value);
                    break;

                case "ANNIVERSARY":
                    contact.Anniversary = ParseDate(prop.Value);
                    break;

                case "NOTE":
                    contact.Notes = prop.Value;
                    break;

                case "TEL":
                    contact.Phones.Add(PhoneNumber.Parse(
                        prop.Value,
                        ParsePhoneKind(prop),
                        prop.IsPreferred));
                    break;

                case "EMAIL":
                    contact.Emails.Add(new EmailAddress
                    {
                        Address = prop.Value,
                        Kind = ParseEmailKind(prop),
                        IsPreferred = prop.IsPreferred,
                    });
                    break;

                case "ADR":
                {
                    var a = SplitStructured(prop.Value);
                    contact.Addresses.Add(new PostalAddress
                    {
                        PoBox = a.Length > 0 ? NullIfEmpty(a[0]) : null,
                        Extended = a.Length > 1 ? NullIfEmpty(a[1]) : null,
                        Street = a.Length > 2 ? NullIfEmpty(a[2]) : null,
                        Locality = a.Length > 3 ? NullIfEmpty(a[3]) : null,
                        Region = a.Length > 4 ? NullIfEmpty(a[4]) : null,
                        PostalCode = a.Length > 5 ? NullIfEmpty(a[5]) : null,
                        Country = a.Length > 6 ? NullIfEmpty(a[6]) : null,
                        Kind = ParseAddressKind(prop),
                    });
                    break;
                }

                case "URL":
                    if (!string.IsNullOrWhiteSpace(prop.Value)) contact.Urls.Add(prop.Value);
                    break;

                case "CATEGORIES":
                    foreach (var c in prop.Value.Split(','))
                        if (!string.IsNullOrWhiteSpace(c)) contact.Categories.Add(c.Trim());
                    break;

                case "PHOTO":
                case "LOGO":
                    TryAttachPhoto(contact, prop);
                    break;

                default:
                    if (prop.Name.StartsWith("X-") && !string.IsNullOrEmpty(prop.Value))
                    {
                        // Preserve vendor extensions verbatim.
                        contact.CustomFields[prop.Name] = prop.Value;
                    }
                    break;
            }
        }

        return seenAny ? contact : null;
    }

    /// <summary>Parsed property representation.</summary>
    internal sealed class VProp
    {
        public string Group { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public List<KeyValuePair<string, string>> Params { get; } = new();

        public bool IsPreferred =>
            Params.Any(kv =>
                (kv.Key.Equals("TYPE", StringComparison.OrdinalIgnoreCase) &&
                 kv.Value.Equals("PREF", StringComparison.OrdinalIgnoreCase)) ||
                kv.Key.Equals("PREF", StringComparison.OrdinalIgnoreCase));

        public IEnumerable<string> Types =>
            Params.Where(kv => kv.Key.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
                  .SelectMany(kv => kv.Value.Split(','));

        public string? GetParam(string name) =>
            Params.FirstOrDefault(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    /// <summary>Parse one logical line into a property record. Returns null on syntactic noise.</summary>
    internal static VProp? ParseProperty(string line, string version)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var colon = FindUnquotedColon(line);
        if (colon <= 0) return null;

        var head = line[..colon];
        var value = line[(colon + 1)..];

        // Split GROUP.NAME;param=...
        var nameEnd = IndexOfAny(head, new[] { ';' });
        var groupAndName = nameEnd < 0 ? head : head[..nameEnd];
        var paramSection = nameEnd < 0 ? "" : head[(nameEnd + 1)..];

        var dot = groupAndName.IndexOf('.');
        var group = dot >= 0 ? groupAndName[..dot] : "";
        var name = (dot >= 0 ? groupAndName[(dot + 1)..] : groupAndName).Trim().ToUpperInvariant();

        var prop = new VProp { Group = group, Name = name };

        // Params (semicolon-separated, may be quoted)
        foreach (var token in SplitParams(paramSection))
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            var eq = token.IndexOf('=');
            string key, val;
            if (eq < 0)
            {
                // vCard 2.1 bare param ("HOME", "WORK", "PREF", "QUOTED-PRINTABLE", ...)
                if (TryClassifyBareParam(token, out var k, out var v))
                {
                    key = k;
                    val = v;
                }
                else
                {
                    key = "TYPE";
                    val = token.Trim();
                }
            }
            else
            {
                key = token[..eq].Trim();
                val = token[(eq + 1)..].Trim().Trim('"');
                if (version == "4.0") val = UnescapeRfc6868(val);
            }
            prop.Params.Add(new KeyValuePair<string, string>(key, val));
        }

        // Decode value per encoding/charset/version
        var encoding = prop.GetParam("ENCODING");
        var charset = prop.GetParam("CHARSET");

        if (string.Equals(encoding, "QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
            value = DecodeQuotedPrintable(value, charset);
        else if (string.Equals(encoding, "BASE64", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(encoding, "B", StringComparison.OrdinalIgnoreCase))
        {
            // Leave as raw base64 — caller handles binary props.
        }

        // Apply value-text escaping for 3.0/4.0
        if (version != "2.1" && encoding is null)
            value = UnescapeText(value);

        return new VProp
        {
            Group = prop.Group,
            Name = prop.Name,
            Value = value,
        }.WithParams(prop.Params);
    }

    private static string[] SplitStructured(string value)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\\' && i + 1 < value.Length) { sb.Append(value[i + 1]); i++; continue; }
            if (ch == ';') { parts.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        parts.Add(sb.ToString());
        return parts.ToArray();
    }

    private static int FindUnquotedColon(string s)
    {
        var inQuote = false;
        for (int i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (ch == ':' && !inQuote) return i;
        }
        return -1;
    }

    private static int IndexOfAny(string s, char[] chars)
    {
        var inQuote = false;
        for (int i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (!inQuote && Array.IndexOf(chars, ch) >= 0) return i;
        }
        return -1;
    }

    private static IEnumerable<string> SplitParams(string s)
    {
        if (string.IsNullOrEmpty(s)) yield break;
        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var ch in s)
        {
            if (ch == '"') { inQuote = !inQuote; sb.Append(ch); continue; }
            if (ch == ';' && !inQuote)
            {
                yield return sb.ToString();
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static bool TryClassifyBareParam(string token, out string key, out string val)
    {
        var t = token.Trim().ToUpperInvariant();
        switch (t)
        {
            case "PREF":
                key = "TYPE"; val = "PREF"; return true;
            case "QUOTED-PRINTABLE":
            case "BASE64":
            case "B":
                key = "ENCODING"; val = t; return true;
            default:
                if (t.StartsWith("CHARSET="))
                {
                    key = "CHARSET"; val = t[8..]; return true;
                }
                key = "TYPE"; val = t; return true;
        }
    }

    /// <summary>RFC 6868: ^n -> newline, ^^ -> ^, ^' -> ".</summary>
    private static string UnescapeRfc6868(string s)
    {
        if (string.IsNullOrEmpty(s) || !s.Contains('^')) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '^' && i + 1 < s.Length)
            {
                switch (s[i + 1])
                {
                    case 'n': sb.Append('\n'); i++; continue;
                    case '^': sb.Append('^'); i++; continue;
                    case '\'': sb.Append('"'); i++; continue;
                }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string UnescapeText(string s)
    {
        if (string.IsNullOrEmpty(s) || !s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                switch (s[i + 1])
                {
                    case 'n':
                    case 'N': sb.Append('\n'); i++; continue;
                    case ',': sb.Append(','); i++; continue;
                    case ';': sb.Append(';'); i++; continue;
                    case '\\': sb.Append('\\'); i++; continue;
                    case ':': sb.Append(':'); i++; continue;
                }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string DecodeQuotedPrintable(string input, string? charset)
    {
        var enc = ResolveEncoding(charset);
        var bytes = new List<byte>();
        var sbAscii = new StringBuilder();

        void Flush()
        {
            if (bytes.Count > 0)
            {
                sbAscii.Append(enc.GetString(bytes.ToArray()));
                bytes.Clear();
            }
        }

        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '=' && i + 2 < input.Length && IsHex(input[i + 1]) && IsHex(input[i + 2]))
            {
                bytes.Add(Convert.ToByte(input.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (c == '=' && i + 1 == input.Length)
            {
                // soft line break
            }
            else
            {
                Flush();
                sbAscii.Append(c);
            }
        }
        Flush();
        return sbAscii.ToString();
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrEmpty(charset)) return Encoding.UTF8;
        try { return Encoding.GetEncoding(charset); }
        catch
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(charset);
            }
            catch { return Encoding.UTF8; }
        }
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static DateOnly? ParseDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var formats = new[] { "yyyy-MM-dd", "yyyyMMdd", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss" };
        if (DateOnly.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return DateOnly.FromDateTime(dt);
        // vCard 4.0 partial date: --MMDD or --MM-DD
        if (s.StartsWith("--") && s.Length >= 6)
        {
            var rest = s[2..].Replace("-", "");
            if (rest.Length >= 4 &&
                int.TryParse(rest[..2], out var m) &&
                int.TryParse(rest.Substring(2, 2), out var dd))
            {
                try { return new DateOnly(2000, m, dd); } catch { return null; }
            }
        }
        return null;
    }

    private static PhoneKind ParsePhoneKind(VProp prop)
    {
        var types = prop.Types.ToList();
        if (types.Any(t => t.Equals("CELL", StringComparison.OrdinalIgnoreCase) ||
                           t.Equals("MOBILE", StringComparison.OrdinalIgnoreCase))) return PhoneKind.Mobile;
        if (types.Any(t => t.Equals("HOME", StringComparison.OrdinalIgnoreCase))) return PhoneKind.Home;
        if (types.Any(t => t.Equals("WORK", StringComparison.OrdinalIgnoreCase))) return PhoneKind.Work;
        if (types.Any(t => t.Equals("FAX", StringComparison.OrdinalIgnoreCase))) return PhoneKind.Fax;
        if (types.Any(t => t.Equals("PAGER", StringComparison.OrdinalIgnoreCase))) return PhoneKind.Pager;
        if (types.Any(t => t.Equals("MAIN", StringComparison.OrdinalIgnoreCase) ||
                           t.Equals("VOICE", StringComparison.OrdinalIgnoreCase))) return PhoneKind.Main;
        return PhoneKind.Other;
    }

    private static EmailKind ParseEmailKind(VProp prop)
    {
        var types = prop.Types.ToList();
        if (types.Any(t => t.Equals("WORK", StringComparison.OrdinalIgnoreCase))) return EmailKind.Work;
        if (types.Any(t => t.Equals("HOME", StringComparison.OrdinalIgnoreCase) ||
                           t.Equals("PERSONAL", StringComparison.OrdinalIgnoreCase))) return EmailKind.Personal;
        return EmailKind.Other;
    }

    private static AddressKind ParseAddressKind(VProp prop)
    {
        var types = prop.Types.ToList();
        if (types.Any(t => t.Equals("HOME", StringComparison.OrdinalIgnoreCase))) return AddressKind.Home;
        if (types.Any(t => t.Equals("WORK", StringComparison.OrdinalIgnoreCase))) return AddressKind.Work;
        return AddressKind.Other;
    }

    private static void TryAttachPhoto(Contact contact, VProp prop)
    {
        if (string.IsNullOrWhiteSpace(prop.Value)) return;

        // 4.0 data URI
        if (prop.Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = prop.Value.IndexOf(',');
            if (comma > 5)
            {
                var meta = prop.Value[5..comma];
                var data = prop.Value[(comma + 1)..];
                var isB64 = meta.IndexOf("base64", StringComparison.OrdinalIgnoreCase) >= 0;
                contact.PhotoMimeType = meta.Split(';')[0];
                if (isB64)
                {
                    try { contact.PhotoBytes = Convert.FromBase64String(data.Replace("\r", "").Replace("\n", "")); }
                    catch { /* skip malformed */ }
                }
            }
            return;
        }

        // 2.1/3.0 inline base64
        var encoding = prop.GetParam("ENCODING");
        if (string.Equals(encoding, "BASE64", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(encoding, "B", StringComparison.OrdinalIgnoreCase))
        {
            try { contact.PhotoBytes = Convert.FromBase64String(prop.Value.Replace(" ", "").Replace("\r", "").Replace("\n", "")); }
            catch { /* skip malformed */ }

            var t = prop.GetParam("TYPE");
            contact.PhotoMimeType = t is null ? null : (t.Contains('/') ? t : $"image/{t.ToLowerInvariant()}");
            return;
        }

        // External URL — skipped (don't fetch)
    }
}

internal static class VPropExtensions
{
    public static VCardImporter.VProp WithParams(
        this VCardImporter.VProp p,
        IEnumerable<KeyValuePair<string, string>> kvs)
    {
        foreach (var kv in kvs) p.Params.Add(kv);
        return p;
    }
}
