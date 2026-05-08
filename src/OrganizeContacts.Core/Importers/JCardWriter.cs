using System.IO;
using System.Text;
using System.Text.Json;
using OrganizeContacts.Core.Models;

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

        if (!string.IsNullOrWhiteSpace(c.FormattedName))
            WriteProp(w, "fn", "text", c.FormattedName!);

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
        if (!string.IsNullOrWhiteSpace(c.Organization)) WriteProp(w, "org", "text", c.Organization!);
        if (!string.IsNullOrWhiteSpace(c.Title)) WriteProp(w, "title", "text", c.Title!);
        if (!string.IsNullOrWhiteSpace(c.Notes)) WriteProp(w, "note", "text", c.Notes!);
        if (c.Birthday.HasValue) WriteProp(w, "bday", "date", c.Birthday.Value.ToString("yyyy-MM-dd"));
        if (c.Anniversary.HasValue) WriteProp(w, "anniversary", "date", c.Anniversary.Value.ToString("yyyy-MM-dd"));

        foreach (var p in c.Phones)
            WritePropTyped(w, "tel", new[] { p.Kind.ToString().ToLowerInvariant() },
                "uri", string.IsNullOrEmpty(p.E164) ? p.Raw : p.E164!);

        foreach (var e in c.Emails)
            WritePropTyped(w, "email", new[] { e.Kind.ToString().ToLowerInvariant() }, "text", e.Address);

        foreach (var u in c.Urls)
            WriteProp(w, "url", "uri", u);

        if (c.Categories.Count > 0)
        {
            WritePropOpen(w, "categories");
            w.WriteStartArray();
            foreach (var cat in c.Categories) w.WriteStringValue(cat);
            w.WriteEndArray();
            w.WriteEndArray();
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

    private static void WritePropTyped(Utf8JsonWriter w, string name, string[] types, string valueType, string value)
    {
        w.WriteStartArray();
        w.WriteStringValue(name);
        w.WriteStartObject();
        w.WritePropertyName("type");
        if (types.Length == 1) w.WriteStringValue(types[0]);
        else
        {
            w.WriteStartArray();
            foreach (var t in types) w.WriteStringValue(t);
            w.WriteEndArray();
        }
        w.WriteEndObject();
        w.WriteStringValue(valueType);
        w.WriteStringValue(value);
        w.WriteEndArray();
    }
}
