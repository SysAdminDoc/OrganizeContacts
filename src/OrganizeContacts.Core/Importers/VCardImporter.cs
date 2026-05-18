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
    static VCardImporter()
    {
        // Register code-pages provider once so QUOTED-PRINTABLE blobs that declare
        // CHARSET=windows-1252 (Outlook for Mac, etc.) decode without racing on first use.
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
        catch { /* test environments without the provider package — ignore */ }
    }

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

        foreach (var c in ParseCards(raw, path)) yield return c;
    }

    /// <summary>Parse a vCard from a string (e.g. the body returned by a CardDAV GET).
    /// Avoids round-tripping through a temp file when the source is already in memory.</summary>
    public IEnumerable<Contact> ParseAll(string vcardText, string sourceLabel = "")
    {
        if (string.IsNullOrEmpty(vcardText)) yield break;
        var raw = new List<string>(vcardText.Length / 64 + 1);
        // Match StreamReader semantics: split on CR, LF, or CRLF.
        var sb = new StringBuilder();
        for (int i = 0; i < vcardText.Length; i++)
        {
            var ch = vcardText[i];
            if (ch == '\r')
            {
                raw.Add(sb.ToString()); sb.Clear();
                if (i + 1 < vcardText.Length && vcardText[i + 1] == '\n') i++;
            }
            else if (ch == '\n')
            {
                raw.Add(sb.ToString()); sb.Clear();
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0) raw.Add(sb.ToString());

        foreach (var c in ParseCards(raw, sourceLabel)) yield return c;
    }

    private static IEnumerable<Contact> ParseCards(IReadOnlyList<string> raw, string sourceLabel)
    {
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
                    var c = ParseCard(card, sourceLabel);
                    if (c is not null) yield return c;
                }
                inCard = false;
                continue;
            }
            if (inCard) card.Add(line);
        }
    }

    /// <summary>
    /// Line unfolding for vCard 2.1/3.0/4.0:
    ///  • RFC 6350 §3.2 — a continuation line begins with WS; trim it and append to the
    ///    previous logical line.
    ///  • RFC 2045 §6.7 — for properties that declared `ENCODING=QUOTED-PRINTABLE`, a
    ///    trailing `=` is a "soft line break". The next line must be appended (without the
    ///    `=`), regardless of leading whitespace. Without this, long QP-encoded values
    ///    from Outlook for Mac / BlackBerry exports were silently truncated at the first
    ///    soft break.
    ///
    /// Defence: if the QP-trailing-equals line is followed by what clearly begins a new
    /// vCard property (uppercase letter / digit / "BEGIN" / "END" with a colon on the
    /// same line), do NOT swallow the next line — that "=" was either a literal trailing
    /// equal in a malformed export or marked the end of the value. Without this guard a
    /// hand-edited card could hide its own EMAIL/TEL lines inside the prior TEL value.
    /// </summary>
    internal static List<string> Unfold(IReadOnlyList<string> raw)
    {
        var unfolded = new List<string>(raw.Count);
        var sb = new StringBuilder();
        foreach (var line in raw)
        {
            // RFC 6350 whitespace-continuation
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && unfolded.Count > 0)
            {
                sb.Clear();
                sb.Append(unfolded[^1]).Append(line, 1, line.Length - 1);
                unfolded[^1] = sb.ToString();
                continue;
            }
            // RFC 2045 quoted-printable soft-line-break.
            if (unfolded.Count > 0 &&
                unfolded[^1].EndsWith('=') &&
                IsQuotedPrintableLine(unfolded[^1]) &&
                !LooksLikePropertyLine(line))
            {
                sb.Clear();
                sb.Append(unfolded[^1], 0, unfolded[^1].Length - 1).Append(line);
                unfolded[^1] = sb.ToString();
                continue;
            }
            unfolded.Add(line);
        }
        return unfolded;
    }

    /// <summary>Heuristic: is <paramref name="line"/> a fresh vCard property line?
    /// True for lines that start with an uppercase letter or digit and contain a `:`
    /// before any `=` (since `=` is the only QP marker we'd expect inside a value).</summary>
    private static bool LooksLikePropertyLine(string line)
    {
        if (line.Length == 0) return false;
        var first = line[0];
        if (!(char.IsAsciiLetterUpper(first) || char.IsAsciiDigit(first))) return false;
        // BEGIN:VCARD / END:VCARD always start a fresh card.
        if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("END:", StringComparison.OrdinalIgnoreCase)) return true;
        var colon = line.IndexOf(':');
        if (colon <= 0) return false;
        // Property name characters must be alnum, '-', or '.' (groups). Allow ';' as that
        // appears in `TEL;TYPE=CELL`.
        for (int i = 0; i < colon; i++)
        {
            var ch = line[i];
            if (char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '.' || ch == ';' || ch == '=' || ch == ',' || ch == '"' || ch == '_')
                continue;
            return false;
        }
        return true;
    }

    private static bool IsQuotedPrintableLine(string line)
    {
        // Cheap textual probe — we only care that the parameter section
        // declares QP encoding before the first ':'. Avoids running the full param
        // tokenizer on every line.
        var colon = line.IndexOf(':');
        if (colon < 0) return false;
        var head = line.AsSpan(0, colon);
        return head.Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
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
                    contact.Uid = UnescapeText(prop.Value);
                    break;

                case "REV":
                    contact.Rev = prop.Value;
                    break;

                case "FN":
                    contact.FormattedName = UnescapeText(prop.Value);
                    seenAny = true;
                    break;

                case "N":
                {
                    var n = SplitStructured(prop.Value);
                    if (n.Length >= 1) contact.FamilyName = NullIfEmpty(UnescapeText(n[0]));
                    if (n.Length >= 2) contact.GivenName = NullIfEmpty(UnescapeText(n[1]));
                    if (n.Length >= 3) contact.AdditionalNames = NullIfEmpty(UnescapeText(n[2]));
                    if (n.Length >= 4) contact.HonorificPrefix = NullIfEmpty(UnescapeText(n[3]));
                    if (n.Length >= 5) contact.HonorificSuffix = NullIfEmpty(UnescapeText(n[4]));
                    seenAny = true;
                    break;
                }

                case "NICKNAME":
                    contact.Nickname = UnescapeText(prop.Value);
                    break;

                case "ORG":
                    contact.Organization = NullIfEmpty(UnescapeText(SplitStructured(prop.Value).FirstOrDefault() ?? string.Empty));
                    break;

                case "TITLE":
                case "ROLE":
                    if (string.IsNullOrEmpty(contact.Title)) contact.Title = UnescapeText(prop.Value);
                    break;

                case "BDAY":
                    contact.Birthday = ParseDate(prop.Value);
                    break;

                case "ANNIVERSARY":
                    contact.Anniversary = ParseDate(prop.Value);
                    break;

                case "NOTE":
                    contact.Notes = UnescapeText(prop.Value);
                    break;

                case "TEL":
                    if (!string.IsNullOrWhiteSpace(prop.Value))
                    {
                        contact.Phones.Add(PhoneNumber.Parse(
                            UnescapeText(prop.Value),
                            ParsePhoneKind(prop),
                            prop.IsPreferred));
                        seenAny = true;
                    }
                    break;

                case "EMAIL":
                    if (!string.IsNullOrWhiteSpace(prop.Value))
                    {
                        contact.Emails.Add(new EmailAddress
                        {
                            Address = UnescapeText(prop.Value),
                            Kind = ParseEmailKind(prop),
                            IsPreferred = prop.IsPreferred,
                        });
                        seenAny = true;
                    }
                    break;

                case "ADR":
                {
                    var a = SplitStructured(prop.Value);
                    contact.Addresses.Add(new PostalAddress
                    {
                        PoBox = a.Length > 0 ? NullIfEmpty(UnescapeText(a[0])) : null,
                        Extended = a.Length > 1 ? NullIfEmpty(UnescapeText(a[1])) : null,
                        Street = a.Length > 2 ? NullIfEmpty(UnescapeText(a[2])) : null,
                        Locality = a.Length > 3 ? NullIfEmpty(UnescapeText(a[3])) : null,
                        Region = a.Length > 4 ? NullIfEmpty(UnescapeText(a[4])) : null,
                        PostalCode = a.Length > 5 ? NullIfEmpty(UnescapeText(a[5])) : null,
                        Country = a.Length > 6 ? NullIfEmpty(UnescapeText(a[6])) : null,
                        Kind = ParseAddressKind(prop),
                    });
                    break;
                }

                case "URL":
                    if (!string.IsNullOrWhiteSpace(prop.Value)) contact.Urls.Add(UnescapeText(prop.Value));
                    break;

                case "CATEGORIES":
                    foreach (var c in SplitEscaped(prop.Value, ','))
                    {
                        var v = UnescapeText(c).Trim();
                        if (!string.IsNullOrWhiteSpace(v)) contact.Categories.Add(v);
                    }
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

        // Note: leave value escaped so structured fields (N, ORG, ADR) split correctly.
        // Per-field handlers call UnescapeText on leaf strings.

        return new VProp
        {
            Group = prop.Group,
            Name = prop.Name,
            Value = value,
        }.WithParams(prop.Params);
    }

    /// <summary>Split <paramref name="value"/> on <paramref name="sep"/>, honouring backslash escapes.</summary>
    internal static List<string> SplitEscaped(string value, char sep)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(value)) return parts;
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\\' && i + 1 < value.Length)
            {
                sb.Append(ch);
                sb.Append(value[i + 1]);
                i++;
                continue;
            }
            if (ch == sep) { parts.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        parts.Add(sb.ToString());
        return parts;
    }

    /// <summary>Split on unescaped <c>;</c> while keeping every backslash escape sequence
    /// intact for the per-field <see cref="UnescapeText"/> pass. The previous implementation
    /// stripped the backslash and emitted only the next char, so a structured value like
    /// <c>Family;Given;\nMiddle</c> arrived at the leaf handlers as <c>nMiddle</c> instead
    /// of a literal newline (because UnescapeText could no longer see the \n escape).</summary>
    private static string[] SplitStructured(string value)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\\' && i + 1 < value.Length)
            {
                // Preserve the escape pair so UnescapeText can decode \n / \, / \; / \\ later.
                sb.Append(ch);
                sb.Append(value[i + 1]);
                i++;
                continue;
            }
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
        catch { return Encoding.UTF8; }
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
                if (isB64)
                {
                    try
                    {
                        contact.PhotoBytes = Convert.FromBase64String(StripBase64Whitespace(data));
                        contact.PhotoMimeType = meta.Split(';')[0];
                        InferMimeFromMagicIfMissing(contact);
                    }
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
            try
            {
                contact.PhotoBytes = Convert.FromBase64String(StripBase64Whitespace(prop.Value));
            }
            catch { /* skip malformed */ return; }

            var t = prop.GetParam("TYPE");
            contact.PhotoMimeType = t is null ? null : (t.Contains('/') ? t : $"image/{t.ToLowerInvariant()}");
            // No TYPE param? Sniff JPEG/PNG magic so the photo isn't orphaned without
            // a mime type — UI / writer fall back to image/jpeg, but having the right
            // mime preserves PNG transparency on round-trip.
            InferMimeFromMagicIfMissing(contact);
            return;
        }

        // External URL — skipped (don't fetch)
    }

    private static string StripBase64Whitespace(string s)
    {
        // Faster + GC-friendlier than chained Replace calls.
        if (s.IndexOfAny(s_b64WhitespaceChars) < 0) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (ch != ' ' && ch != '\t' && ch != '\r' && ch != '\n') sb.Append(ch);
        return sb.ToString();
    }
    private static readonly char[] s_b64WhitespaceChars = new[] { ' ', '\t', '\r', '\n' };

    private static void InferMimeFromMagicIfMissing(Contact contact)
    {
        if (!string.IsNullOrEmpty(contact.PhotoMimeType)) return;
        var bytes = contact.PhotoBytes;
        if (bytes is null || bytes.Length < 4) return;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) contact.PhotoMimeType = "image/jpeg";
        else if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) contact.PhotoMimeType = "image/png";
        else if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) contact.PhotoMimeType = "image/gif";
        else if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 &&
                 bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            contact.PhotoMimeType = "image/webp";
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
