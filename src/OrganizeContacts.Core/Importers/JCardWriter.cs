using System.IO;
using System.Text;
using System.Text.Json;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Photos;

namespace OrganizeContacts.Core.Importers;

/// <summary>jCard (RFC 7095) writer. Produces an array of jCards (one per contact).</summary>
public sealed class JCardWriter
{
    public async Task WriteFileAsync(string path, IReadOnlyList<Contact> contacts, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
        w.WriteStartArray();
        foreach (var c in contacts)
        {
            ct.ThrowIfCancellationRequested();
            WriteOne(w, c);
        }
        w.WriteEndArray();
        await w.FlushAsync(ct);
    }

    private static void WriteOne(Utf8JsonWriter w, Contact c)
    {
        w.WriteStartArray();
        w.WriteStringValue("vcard");
        w.WriteStartArray();

        WriteProp(w, "version", "text", "4.0");
        if (!string.IsNullOrWhiteSpace(c.Uid)) WriteProp(w, "uid", "text", c.Uid!);

        var formattedName = string.IsNullOrWhiteSpace(c.FormattedName) ? c.DisplayName : c.FormattedName!;
        WriteProp(w, "fn", "text", formattedName);

        WritePropOpen(w, "n");
        w.WriteStartArray();
        w.WriteStringValue(c.FamilyName ?? "");
        w.WriteStringValue(c.GivenName ?? "");
        w.WriteStringValue(c.AdditionalNames ?? "");
        w.WriteStringValue(c.HonorificPrefix ?? "");
        w.WriteStringValue(c.HonorificSuffix ?? "");
        w.WriteEndArray();
        w.WriteEndArray();

        if (!string.IsNullOrWhiteSpace(c.Nickname)) WriteProp(w, "nickname", "text", c.Nickname!);
        if (!string.IsNullOrWhiteSpace(c.Organization))
            WriteStructuredTextProp(w, "org", new[] { c.Organization! });
        if (!string.IsNullOrWhiteSpace(c.Title)) WriteProp(w, "title", "text", c.Title!);
        if (!string.IsNullOrWhiteSpace(c.Notes)) WriteProp(w, "note", "text", c.Notes!);
        if (c.Birthday.HasValue) WriteProp(w, "bday", "date", c.Birthday.Value.ToString("yyyy-MM-dd"));
        if (c.Anniversary.HasValue) WriteProp(w, "anniversary", "date", c.Anniversary.Value.ToString("yyyy-MM-dd"));

        foreach (var p in c.Phones)
        {
            var number = string.IsNullOrEmpty(p.E164) ? p.Raw : p.E164!;
            if (!number.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) number = "tel:" + number;
            WritePropTyped(w, "tel", PhoneTypes(p.Kind), p.IsPreferred, "uri", number);
        }

        foreach (var e in c.Emails)
            WritePropTyped(w, "email", EmailTypes(e.Kind), e.IsPreferred, "text", e.Address);

        foreach (var a in c.Addresses)
        {
            w.WriteStartArray();
            w.WriteStringValue("adr");
            WriteParameters(w, AddressTypes(a.Kind), a.IsPreferred);
            w.WriteStringValue("text");
            w.WriteStartArray();
            w.WriteStringValue(a.PoBox ?? "");
            w.WriteStringValue(a.Extended ?? "");
            w.WriteStringValue(a.Street ?? "");
            w.WriteStringValue(a.Locality ?? "");
            w.WriteStringValue(a.Region ?? "");
            w.WriteStringValue(a.PostalCode ?? "");
            w.WriteStringValue(a.Country ?? "");
            w.WriteEndArray();
            w.WriteEndArray();
        }

        foreach (var u in c.Urls)
            WriteProp(w, "url", "uri", u);

        if (c.Categories.Count > 0)
        {
            w.WriteStartArray();
            w.WriteStringValue("categories");
            w.WriteStartObject(); w.WriteEndObject();
            w.WriteStringValue("text");
            foreach (var cat in c.Categories) w.WriteStringValue(cat);
            w.WriteEndArray();
        }

        if (c.PhotoBytes is { Length: > 0 })
        {
            var mime = PhotoSanitizer.NormalizeImageMimeType(c.PhotoMimeType) ??
                       PhotoSanitizer.InferMimeType(c.PhotoBytes) ??
                       "image/jpeg";
            WriteProp(w, "photo", "uri", $"data:{mime};base64,{Convert.ToBase64String(c.PhotoBytes)}");
        }

        if (c.CustomFields.TryGetValue(VCardImporter.PreservedPhotoUriField, out var photoUri) &&
            Uri.TryCreate(photoUri, UriKind.Absolute, out _))
            WriteProp(w, "photo", "uri", photoUri);

        foreach (var field in c.CustomFields)
        {
            if (field.Key.Equals(VCardImporter.PreservedPhotoUriField, StringComparison.OrdinalIgnoreCase)) continue;
            var name = field.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase)
                ? field.Key
                : "X-" + field.Key;
            WriteProp(w, name.ToLowerInvariant(), "unknown", field.Value);
        }

        WriteProp(w, "rev", "timestamp", c.Rev ?? c.UpdatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));

        w.WriteEndArray(); // properties
        w.WriteEndArray(); // jcard
    }

    private static void WriteProp(Utf8JsonWriter w, string name, string type, string value)
    {
        w.WriteStartArray();
        w.WriteStringValue(name);
        w.WriteStartObject(); w.WriteEndObject(); // empty params
        w.WriteStringValue(type);
        w.WriteStringValue(value);
        w.WriteEndArray();
    }

    private static void WritePropOpen(Utf8JsonWriter w, string name)
    {
        w.WriteStartArray();
        w.WriteStringValue(name);
        w.WriteStartObject(); w.WriteEndObject();
        w.WriteStringValue("text");
    }

    private static void WriteStructuredTextProp(Utf8JsonWriter w, string name, IEnumerable<string> components)
    {
        WritePropOpen(w, name);
        w.WriteStartArray();
        foreach (var component in components) w.WriteStringValue(component);
        w.WriteEndArray();
        w.WriteEndArray();
    }

    private static void WritePropTyped(
        Utf8JsonWriter w,
        string name,
        IReadOnlyList<string> types,
        bool preferred,
        string valueType,
        string value)
    {
        w.WriteStartArray();
        w.WriteStringValue(name);
        WriteParameters(w, types, preferred);
        w.WriteStringValue(valueType);
        w.WriteStringValue(value);
        w.WriteEndArray();
    }

    private static void WriteParameters(Utf8JsonWriter w, IReadOnlyList<string> types, bool preferred)
    {
        w.WriteStartObject();
        if (types.Count == 1)
        {
            w.WriteString("type", types[0]);
        }
        else if (types.Count > 1)
        {
            w.WritePropertyName("type");
            w.WriteStartArray();
            foreach (var type in types) w.WriteStringValue(type);
            w.WriteEndArray();
        }
        if (preferred) w.WriteString("pref", "1");
        w.WriteEndObject();
    }

    private static IReadOnlyList<string> PhoneTypes(PhoneKind kind) => kind switch
    {
        PhoneKind.Mobile => new[] { "cell" },
        PhoneKind.Home => new[] { "home" },
        PhoneKind.Work => new[] { "work" },
        PhoneKind.Fax => new[] { "fax" },
        PhoneKind.Pager => new[] { "pager" },
        PhoneKind.Main => new[] { "voice" },
        _ => Array.Empty<string>(),
    };

    private static IReadOnlyList<string> EmailTypes(EmailKind kind) => kind switch
    {
        EmailKind.Personal => new[] { "home" },
        EmailKind.Work => new[] { "work" },
        _ => Array.Empty<string>(),
    };

    private static IReadOnlyList<string> AddressTypes(AddressKind kind) => kind switch
    {
        AddressKind.Home => new[] { "home" },
        AddressKind.Work => new[] { "work" },
        _ => Array.Empty<string>(),
    };
}
