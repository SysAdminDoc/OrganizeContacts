using System.IO;
using System.Text.Json;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Photos;

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
                case "FN":
                    contact.FormattedName = value;
                    seen |= !string.IsNullOrWhiteSpace(value);
                    break;
                case "N":
                    {
                        var arr = prop[3];
                        if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() >= 2)
                        {
                            contact.FamilyName = ExtractComponent(arr[0]);
                            contact.GivenName = ExtractComponent(arr[1]);
                            if (arr.GetArrayLength() >= 3) contact.AdditionalNames = ExtractComponent(arr[2]);
                            if (arr.GetArrayLength() >= 4) contact.HonorificPrefix = ExtractComponent(arr[3]);
                            if (arr.GetArrayLength() >= 5) contact.HonorificSuffix = ExtractComponent(arr[4]);
                            seen |= !string.IsNullOrWhiteSpace(contact.DisplayName);
                        }
                        break;
                    }
                case "NICKNAME":
                    contact.Nickname = value;
                    seen |= !string.IsNullOrWhiteSpace(value);
                    break;
                case "ORG":
                    contact.Organization = ExtractFirstComponent(prop[3]);
                    seen |= !string.IsNullOrWhiteSpace(contact.Organization);
                    break;
                case "TITLE":
                case "ROLE":
                    if (string.IsNullOrWhiteSpace(contact.Title)) contact.Title = value;
                    seen |= !string.IsNullOrWhiteSpace(value);
                    break;
                case "BDAY":
                    if (GoogleCsvImporter.TryParseCsvDate(value ?? string.Empty, out var bd))
                    {
                        contact.Birthday = bd;
                        seen = true;
                    }
                    break;
                case "ANNIVERSARY":
                    if (GoogleCsvImporter.TryParseCsvDate(value ?? string.Empty, out var ad))
                    {
                        contact.Anniversary = ad;
                        seen = true;
                    }
                    break;
                case "NOTE":
                    contact.Notes = value;
                    seen |= !string.IsNullOrWhiteSpace(value);
                    break;
                case "URL":
                    if (!string.IsNullOrWhiteSpace(value)) { contact.Urls.Add(value!); seen = true; }
                    break;
                case "TEL":
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        var raw = value!.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                            ? value[4..]
                            : value;
                        contact.Phones.Add(PhoneNumber.Parse(
                            raw,
                            ParsePhoneKind(prop[1]),
                            IsPreferred(prop[1])));
                        seen = true;
                    }
                    break;
                case "EMAIL":
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        contact.Emails.Add(new EmailAddress
                        {
                            Address = value!,
                            Kind = ParseEmailKind(prop[1]),
                            IsPreferred = IsPreferred(prop[1]),
                        });
                        seen = true;
                    }
                    break;
                case "ADR":
                    {
                        var arr = prop[3];
                        if (arr.ValueKind != JsonValueKind.Array) break;
                        var address = new PostalAddress
                        {
                            PoBox = ComponentAt(arr, 0),
                            Extended = ComponentAt(arr, 1),
                            Street = ComponentAt(arr, 2),
                            Locality = ComponentAt(arr, 3),
                            Region = ComponentAt(arr, 4),
                            PostalCode = ComponentAt(arr, 5),
                            Country = ComponentAt(arr, 6),
                            Kind = ParseAddressKind(prop[1]),
                            IsPreferred = IsPreferred(prop[1]),
                        };
                        if (address.OneLine.Length > 0 || !string.IsNullOrWhiteSpace(address.PoBox) ||
                            !string.IsNullOrWhiteSpace(address.Extended))
                        {
                            contact.Addresses.Add(address);
                            seen = true;
                        }
                        break;
                    }
                case "UID":
                    contact.Uid = value;
                    break;
                case "REV":
                    contact.Rev = value;
                    break;
                case "CATEGORIES":
                    // RFC 7095 represents each category as another property-array element.
                    // Also accept the older nested-array and comma-string shapes emitted by
                    // earlier OrganizeContacts releases and third-party exporters.
                    for (var i = 3; i < prop.GetArrayLength(); i++)
                    {
                        AddCategories(contact.Categories, prop[i]);
                    }
                    seen |= contact.Categories.Count > 0;
                    break;
                case "PHOTO":
                case "LOGO":
                    if (TryAttachPhoto(contact, value)) seen = true;
                    else if (Uri.TryCreate(value, UriKind.Absolute, out _))
                    {
                        contact.CustomFields[VCardImporter.PreservedPhotoUriField] = value!;
                        seen = true;
                    }
                    break;
                default:
                    if (name.StartsWith("X-", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(value))
                    {
                        contact.CustomFields[name] = value;
                        seen = true;
                    }
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
        JsonValueKind.Array => value.GetArrayLength() > 0
            ? string.Join(';', value.EnumerateArray().Select(ExtractComponent))
            : null,
        _ => null,
    };

    private static string? ExtractComponent(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => NullIfEmpty(value.GetString()),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Array => NullIfEmpty(string.Join(',', value.EnumerateArray()
            .Select(ExtractComponent)
            .Where(x => !string.IsNullOrWhiteSpace(x)))),
        _ => null,
    };

    private static string? ExtractFirstComponent(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return ExtractComponent(value);
        return value.GetArrayLength() == 0 ? null : ExtractComponent(value[0]);
    }

    private static string? ComponentAt(JsonElement array, int index) =>
        index < array.GetArrayLength() ? ExtractComponent(array[index]) : null;

    private static void AddCategories(List<string> categories, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) AddCategories(categories, item);
            return;
        }

        var text = ExtractComponent(value);
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var item in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            categories.Add(item);
    }

    private static bool TryAttachPhoto(Contact contact, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;

        var comma = value.IndexOf(',');
        if (comma <= 5) return false;
        var metadata = value[5..comma];
        if (!metadata.Split(';').Any(x => x.Equals("base64", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!PhotoSanitizer.TryDecodeBase64(value[(comma + 1)..], out var bytes)) return false;
        var mime = PhotoSanitizer.NormalizeImageMimeType(metadata.Split(';', 2)[0]) ??
                   PhotoSanitizer.InferMimeType(bytes);
        if (mime is null) return false;
        contact.PhotoBytes = bytes;
        contact.PhotoMimeType = mime;
        return true;
    }

    private static PhoneKind ParsePhoneKind(JsonElement parameters)
    {
        var types = ParameterValues(parameters, "type")
            .Where(x => x is not null)
            .Select(x => x!.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
        if (types.Contains("FAX")) return PhoneKind.Fax;
        if (types.Contains("CELL") || types.Contains("MOBILE")) return PhoneKind.Mobile;
        if (types.Contains("HOME")) return PhoneKind.Home;
        if (types.Contains("WORK")) return PhoneKind.Work;
        if (types.Contains("PAGER")) return PhoneKind.Pager;
        if (types.Contains("MAIN") || types.Contains("VOICE")) return PhoneKind.Main;
        return PhoneKind.Other;
    }

    private static EmailKind ParseEmailKind(JsonElement parameters)
    {
        foreach (var t in ParameterValues(parameters, "type"))
        {
            switch (t?.ToUpperInvariant())
            {
                case "WORK": return EmailKind.Work;
                case "HOME": case "PERSONAL": return EmailKind.Personal;
            }
        }
        return EmailKind.Other;
    }

    private static AddressKind ParseAddressKind(JsonElement parameters)
    {
        foreach (var type in ParameterValues(parameters, "type"))
        {
            if (type.Equals("home", StringComparison.OrdinalIgnoreCase)) return AddressKind.Home;
            if (type.Equals("work", StringComparison.OrdinalIgnoreCase)) return AddressKind.Work;
        }
        return AddressKind.Other;
    }

    private static bool IsPreferred(JsonElement parameters)
    {
        if (ParameterValues(parameters, "type")
            .Any(x => x.Equals("pref", StringComparison.OrdinalIgnoreCase))) return true;

        return ParameterValues(parameters, "pref").Any(x =>
            int.TryParse(x, out var rank) && rank is >= 1 and <= 100);
    }

    private static IEnumerable<string> ParameterValues(JsonElement parameters, string name)
    {
        if (parameters.ValueKind != JsonValueKind.Object) yield break;
        JsonElement value = default;
        var found = false;
        foreach (var property in parameters.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            found = true;
            break;
        }
        if (!found) yield break;

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var text = ExtractComponent(item);
                if (!string.IsNullOrWhiteSpace(text)) yield return text;
            }
            yield break;
        }

        var single = ExtractComponent(value);
        if (!string.IsNullOrWhiteSpace(single)) yield return single;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
