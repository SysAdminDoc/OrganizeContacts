using System.IO;
using System.Text.Json;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

/// <summary>
/// jCard (RFC 7095) importer — vCard 4.0 expressed as JSON.
/// Accepts a single jCard array `["vcard", [...properties]]` or a containing array of jCards.
/// </summary>
public sealed class JCardImporter : IContactImporter
{
    public string Name => "jCard (RFC 7095)";
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".jcard", ".jcf", ".json" };

    public bool CanRead(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public async IAsyncEnumerable<Contact> ReadAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        var raw = await File.ReadAllTextAsync(path, ct);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 &&
            root[0].ValueKind == JsonValueKind.Array)
        {
            // Containing array of jCards
            foreach (var card in root.EnumerateArray())
            {
                var c = ParseJCard(card, path);
                if (c is not null) yield return c;
            }
        }
        else
        {
            var c = ParseJCard(root, path);
            if (c is not null) yield return c;
        }
    }

    private static Contact? ParseJCard(JsonElement el, string source)
    {
        if (el.ValueKind != JsonValueKind.Array) return null;
        if (el.GetArrayLength() < 2) return null;
        if (!string.Equals(el[0].GetString(), "vcard", StringComparison.OrdinalIgnoreCase)) return null;

        var contact = new Contact { SourceFile = source, SourceFormat = "jCard" };
        var seen = false;

        foreach (var prop in el[1].EnumerateArray())
        {
            if (prop.ValueKind != JsonValueKind.Array || prop.GetArrayLength() < 4) continue;
            var name = prop[0].GetString()?.ToUpperInvariant();
            var value = ExtractValue(prop[3]);
            if (string.IsNullOrEmpty(name)) continue;

            switch (name)
            {
                case "FN": contact.FormattedName = value; seen = true; break;
                case "N":
                {
                    var arr = prop[3];
                    if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() >= 2)
                    {
                        contact.FamilyName = arr[0].GetString();
                        contact.GivenName = arr[1].GetString();
                        if (arr.GetArrayLength() >= 3) contact.AdditionalNames = arr[2].GetString();
                        if (arr.GetArrayLength() >= 4) contact.HonorificPrefix = arr[3].GetString();
                        if (arr.GetArrayLength() >= 5) contact.HonorificSuffix = arr[4].GetString();
                        seen = true;
                    }
                    break;
                }
                case "NICKNAME": contact.Nickname = value; break;
                case "ORG": contact.Organization = value; break;
                case "TITLE": contact.Title = value; break;
                case "BDAY":
                    if (DateOnly.TryParse(value, out var bd)) contact.Birthday = bd;
                    break;
                case "ANNIVERSARY":
                    if (DateOnly.TryParse(value, out var ad)) contact.Anniversary = ad;
                    break;
                case "NOTE": contact.Notes = value; break;
                case "URL":
                    if (!string.IsNullOrWhiteSpace(value)) contact.Urls.Add(value!);
                    break;
                case "TEL":
                    contact.Phones.Add(PhoneNumber.Parse(value ?? "", ParsePhoneKind(prop[1])));
                    break;
                case "EMAIL":
                    contact.Emails.Add(new EmailAddress
                    {
                        Address = value ?? "",
                        Kind = ParseEmailKind(prop[1]),
                    });
                    break;
                case "UID":
                    contact.Uid = value;
                    break;
                case "REV":
                    contact.Rev = value;
                    break;
                case "CATEGORIES":
                    if (prop[3].ValueKind == JsonValueKind.Array)
                        foreach (var ce in prop[3].EnumerateArray())
                            if (ce.ValueKind == JsonValueKind.String) contact.Categories.Add(ce.GetString()!);
                    else if (!string.IsNullOrEmpty(value))
                        foreach (var ct in value!.Split(',')) contact.Categories.Add(ct.Trim());
                    break;
            }
        }
        return seen ? contact : null;
    }

    private static string? ExtractValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Array => value.GetArrayLength() > 0 && value[0].ValueKind == JsonValueKind.String
            ? string.Join(';', value.EnumerateArray().Select(e => e.GetString()))
            : null,
        _ => null,
    };

    private static PhoneKind ParsePhoneKind(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object) return PhoneKind.Other;
        if (!parameters.TryGetProperty("type", out var type)) return PhoneKind.Other;
        var types = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(e => e.GetString())
            : new[] { type.GetString() };
        foreach (var t in types)
        {
            switch (t?.ToUpperInvariant())
            {
                case "CELL": case "MOBILE": return PhoneKind.Mobile;
                case "HOME": return PhoneKind.Home;
                case "WORK": return PhoneKind.Work;
                case "FAX": return PhoneKind.Fax;
                case "PAGER": return PhoneKind.Pager;
                case "MAIN": case "VOICE": return PhoneKind.Main;
            }
        }
        return PhoneKind.Other;
    }

    private static EmailKind ParseEmailKind(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object) return EmailKind.Other;
        if (!parameters.TryGetProperty("type", out var type)) return EmailKind.Other;
        var types = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(e => e.GetString())
            : new[] { type.GetString() };
        foreach (var t in types)
        {
            switch (t?.ToUpperInvariant())
            {
                case "WORK": return EmailKind.Work;
                case "HOME": case "PERSONAL": return EmailKind.Personal;
            }
        }
        return EmailKind.Other;
    }
}
