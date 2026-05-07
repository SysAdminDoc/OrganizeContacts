using System.Globalization;
using System.Text;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

public sealed class VCardImporter : IContactImporter
{
    public string Name => "vCard 3.0";
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".vcf", ".vcard" };

    public bool CanRead(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public async IAsyncEnumerable<Contact> ReadAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        var buffer = new List<string>();
        var inCard = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            if (line.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                inCard = true;
                buffer.Clear();
                continue;
            }

            if (line.StartsWith("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (inCard && buffer.Count > 0)
                {
                    var card = Parse(Unfold(buffer), path);
                    if (card is not null) yield return card;
                }
                inCard = false;
                continue;
            }

            if (inCard) buffer.Add(line);
        }
    }

    private static List<string> Unfold(List<string> raw)
    {
        var unfolded = new List<string>();
        foreach (var line in raw)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && unfolded.Count > 0)
                unfolded[^1] += line[1..];
            else
                unfolded.Add(line);
        }
        return unfolded;
    }

    private static Contact? Parse(List<string> lines, string sourceFile)
    {
        var contact = new Contact { SourceFile = sourceFile, SourceFormat = "vCard" };
        var seenAny = false;

        foreach (var line in lines)
        {
            var sepIdx = line.IndexOf(':');
            if (sepIdx <= 0) continue;
            var prop = line[..sepIdx];
            var value = line[(sepIdx + 1)..];

            var parts = prop.Split(';');
            var name = parts[0].ToUpperInvariant();
            var paramTokens = parts.Skip(1).ToArray();

            switch (name)
            {
                case "FN":
                    contact.FormattedName = Decode(value, paramTokens);
                    seenAny = true;
                    break;
                case "N":
                    var n = Decode(value, paramTokens).Split(';');
                    if (n.Length >= 1) contact.FamilyName = NullIfEmpty(n[0]);
                    if (n.Length >= 2) contact.GivenName = NullIfEmpty(n[1]);
                    if (n.Length >= 3) contact.AdditionalNames = NullIfEmpty(n[2]);
                    if (n.Length >= 4) contact.HonorificPrefix = NullIfEmpty(n[3]);
                    if (n.Length >= 5) contact.HonorificSuffix = NullIfEmpty(n[4]);
                    seenAny = true;
                    break;
                case "NICKNAME":
                    contact.Nickname = Decode(value, paramTokens);
                    break;
                case "ORG":
                    contact.Organization = Decode(value, paramTokens).Split(';')[0];
                    break;
                case "TITLE":
                    contact.Title = Decode(value, paramTokens);
                    break;
                case "BDAY":
                    if (DateOnly.TryParseExact(
                            value, new[] { "yyyy-MM-dd", "yyyyMMdd" },
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var bday))
                        contact.Birthday = bday;
                    break;
                case "NOTE":
                    contact.Notes = Decode(value, paramTokens);
                    break;
                case "TEL":
                    contact.Phones.Add(PhoneNumber.Parse(
                        Decode(value, paramTokens),
                        ParsePhoneKind(paramTokens),
                        IsPreferred(paramTokens)));
                    break;
                case "EMAIL":
                    contact.Emails.Add(new EmailAddress
                    {
                        Address = Decode(value, paramTokens),
                        Kind = ParseEmailKind(paramTokens),
                        IsPreferred = IsPreferred(paramTokens),
                    });
                    break;
                case "ADR":
                    var a = Decode(value, paramTokens).Split(';');
                    contact.Addresses.Add(new PostalAddress
                    {
                        PoBox = a.Length > 0 ? NullIfEmpty(a[0]) : null,
                        Extended = a.Length > 1 ? NullIfEmpty(a[1]) : null,
                        Street = a.Length > 2 ? NullIfEmpty(a[2]) : null,
                        Locality = a.Length > 3 ? NullIfEmpty(a[3]) : null,
                        Region = a.Length > 4 ? NullIfEmpty(a[4]) : null,
                        PostalCode = a.Length > 5 ? NullIfEmpty(a[5]) : null,
                        Country = a.Length > 6 ? NullIfEmpty(a[6]) : null,
                        Kind = ParseAddressKind(paramTokens),
                    });
                    break;
                case "URL":
                    contact.Urls.Add(Decode(value, paramTokens));
                    break;
                case "CATEGORIES":
                    foreach (var c in Decode(value, paramTokens).Split(','))
                        if (!string.IsNullOrWhiteSpace(c)) contact.Categories.Add(c.Trim());
                    break;
            }
        }

        return seenAny ? contact : null;
    }

    private static string Decode(string value, string[] paramTokens)
    {
        var encoding = paramTokens.FirstOrDefault(p =>
            p.StartsWith("ENCODING=", StringComparison.OrdinalIgnoreCase))?[9..];

        if (string.Equals(encoding, "QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
            value = DecodeQuotedPrintable(value);

        return value
            .Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\\,", ",")
            .Replace("\\;", ";")
            .Replace("\\\\", "\\");
    }

    private static string DecodeQuotedPrintable(string input)
    {
        var sb = new StringBuilder(input.Length);
        var bytes = new List<byte>();

        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '=' && i + 2 < input.Length &&
                IsHex(input[i + 1]) && IsHex(input[i + 2]))
            {
                bytes.Add(Convert.ToByte(input.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                if (bytes.Count > 0)
                {
                    sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
                    bytes.Clear();
                }
                sb.Append(input[i]);
            }
        }

        if (bytes.Count > 0) sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
        return sb.ToString();
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool IsPreferred(string[] paramTokens) =>
        paramTokens.Any(p =>
            p.Equals("PREF", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("TYPE=PREF", StringComparison.OrdinalIgnoreCase));

    private static PhoneKind ParsePhoneKind(string[] paramTokens)
    {
        var types = ExtractTypes(paramTokens);
        if (types.Contains("CELL", StringComparer.OrdinalIgnoreCase) ||
            types.Contains("MOBILE", StringComparer.OrdinalIgnoreCase)) return PhoneKind.Mobile;
        if (types.Contains("HOME", StringComparer.OrdinalIgnoreCase)) return PhoneKind.Home;
        if (types.Contains("WORK", StringComparer.OrdinalIgnoreCase)) return PhoneKind.Work;
        if (types.Contains("FAX", StringComparer.OrdinalIgnoreCase)) return PhoneKind.Fax;
        if (types.Contains("PAGER", StringComparer.OrdinalIgnoreCase)) return PhoneKind.Pager;
        if (types.Contains("MAIN", StringComparer.OrdinalIgnoreCase)) return PhoneKind.Main;
        return PhoneKind.Other;
    }

    private static EmailKind ParseEmailKind(string[] paramTokens)
    {
        var types = ExtractTypes(paramTokens);
        if (types.Contains("WORK", StringComparer.OrdinalIgnoreCase)) return EmailKind.Work;
        if (types.Contains("HOME", StringComparer.OrdinalIgnoreCase) ||
            types.Contains("PERSONAL", StringComparer.OrdinalIgnoreCase)) return EmailKind.Personal;
        return EmailKind.Other;
    }

    private static AddressKind ParseAddressKind(string[] paramTokens)
    {
        var types = ExtractTypes(paramTokens);
        if (types.Contains("HOME", StringComparer.OrdinalIgnoreCase)) return AddressKind.Home;
        if (types.Contains("WORK", StringComparer.OrdinalIgnoreCase)) return AddressKind.Work;
        return AddressKind.Other;
    }

    private static List<string> ExtractTypes(string[] paramTokens)
    {
        var types = new List<string>();
        foreach (var token in paramTokens)
        {
            if (token.StartsWith("TYPE=", StringComparison.OrdinalIgnoreCase))
                types.AddRange(token[5..].Split(','));
            else if (!token.Contains('='))
                types.AddRange(token.Split(','));
        }
        return types;
    }
}
