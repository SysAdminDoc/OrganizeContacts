using System.IO;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public class VCardImporterTests
{
    private static async Task<List<Core.Models.Contact>> ReadFromString(string vcard)
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-test-{Guid.NewGuid():N}.vcf");
        await File.WriteAllTextAsync(path, vcard);
        try
        {
            var importer = new VCardImporter();
            var list = new List<Core.Models.Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            return list;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task Parses_basic_v3_card()
    {
        var src = """
            BEGIN:VCARD
            VERSION:3.0
            FN:John Doe
            N:Doe;John;;;
            EMAIL;TYPE=WORK:john@example.com
            TEL;TYPE=CELL:+15551234567
            END:VCARD
            """;
        var contacts = await ReadFromString(src);
        Assert.Single(contacts);
        var c = contacts[0];
        Assert.Equal("John Doe", c.FormattedName);
        Assert.Equal("Doe", c.FamilyName);
        Assert.Equal("John", c.GivenName);
        Assert.Single(c.Emails);
        Assert.Equal("john@example.com", c.Emails[0].Address);
        Assert.Single(c.Phones);
        Assert.Equal(Core.Models.PhoneKind.Mobile, c.Phones[0].Kind);
    }

    [Fact]
    public async Task Captures_uid_and_rev()
    {
        var src = """
            BEGIN:VCARD
            VERSION:3.0
            UID:urn:uuid:1234
            REV:2026-04-01T12:00:00Z
            FN:Jane
            END:VCARD
            """;
        var c = (await ReadFromString(src))[0];
        Assert.Equal("urn:uuid:1234", c.Uid);
        Assert.Equal("2026-04-01T12:00:00Z", c.Rev);
    }

    [Fact]
    public async Task Decodes_quoted_printable_2_1()
    {
        var src = """
            BEGIN:VCARD
            VERSION:2.1
            FN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Andr=C3=A9
            N:Doe;Andr=C3=A9;;;
            END:VCARD
            """;
        var c = (await ReadFromString(src))[0];
        Assert.Equal("André", c.FormattedName);
    }

    [Fact]
    public async Task Preserves_x_custom_fields()
    {
        var src = """
            BEGIN:VCARD
            VERSION:3.0
            FN:Test
            X-SKYPE-USERNAME:matt
            X-LINKEDIN:https://linkedin.com/in/example
            END:VCARD
            """;
        var c = (await ReadFromString(src))[0];
        Assert.Contains("X-SKYPE-USERNAME", c.CustomFields.Keys);
        Assert.Contains("X-LINKEDIN", c.CustomFields.Keys);
        Assert.Equal("matt", c.CustomFields["X-SKYPE-USERNAME"]);
    }

    [Fact]
    public async Task Unfolds_continuation_lines()
    {
        var src = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Long Name Goes\r\n  Here\r\nEND:VCARD\r\n";
        var c = (await ReadFromString(src))[0];
        Assert.Equal("Long Name Goes Here", c.FormattedName);
    }

    [Fact]
    public async Task Handles_grouped_properties()
    {
        var src = """
            BEGIN:VCARD
            VERSION:3.0
            FN:Group Test
            item1.TEL;TYPE=CELL:5551234
            item1.X-ABLABEL:Direct
            END:VCARD
            """;
        var c = (await ReadFromString(src))[0];
        Assert.Single(c.Phones);
        Assert.Equal(Core.Models.PhoneKind.Mobile, c.Phones[0].Kind);
    }

    [Fact]
    public async Task Decodes_text_escapes_in_3_0()
    {
        var src = """
            BEGIN:VCARD
            VERSION:3.0
            FN:Multi\nline; comma\, escaped
            END:VCARD
            """;
        var c = (await ReadFromString(src))[0];
        Assert.Contains("\n", c.FormattedName);
        Assert.Contains(",", c.FormattedName);
        Assert.Contains(";", c.FormattedName);
    }

    [Fact]
    public async Task Returns_multiple_cards_per_file()
    {
        var src = """
            BEGIN:VCARD
            VERSION:3.0
            FN:Alice
            END:VCARD
            BEGIN:VCARD
            VERSION:4.0
            FN:Bob
            END:VCARD
            """;
        var list = await ReadFromString(src);
        Assert.Equal(2, list.Count);
        Assert.Equal("Alice", list[0].FormattedName);
        Assert.Equal("Bob", list[1].FormattedName);
        Assert.Equal("vCard 4.0", list[1].SourceFormat);
    }

    [Fact]
    public async Task Parses_vcard4_tel_uri_numeric_preference_and_combined_types()
    {
        var src = "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Fax\r\n" +
                  "TEL;VALUE=uri;TYPE=work,fax;PREF=2:tel:+1-202-555-0100%20ext\r\n" +
                  "TEL;VALUE=uri;PREF=0:tel:555-0101\r\nEND:VCARD\r\n";

        var contact = Assert.Single(await ReadFromString(src));

        Assert.Equal(PhoneKind.Fax, contact.Phones[0].Kind);
        Assert.True(contact.Phones[0].IsPreferred);
        Assert.Equal("+1-202-555-0100 ext", contact.Phones[0].Raw);
        Assert.False(contact.Phones[1].IsPreferred);
    }

    [Fact]
    public async Task Keeps_embedded_photo_only_card()
    {
        var photo = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01 };
        var src = "BEGIN:VCARD\r\nVERSION:4.0\r\n" +
                  $"PHOTO:data:image/jpeg;base64,{Convert.ToBase64String(photo)}\r\n" +
                  "END:VCARD\r\n";

        var contact = Assert.Single(await ReadFromString(src));

        Assert.Equal(photo, contact.PhotoBytes);
        Assert.Equal("image/jpeg", contact.PhotoMimeType);
    }

    [Fact]
    public async Task Unescapes_vendor_extension_text_once()
    {
        var src = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Extension\r\n" +
                  "X-TEST:one\\,two\\;three\\\\four\\nfive\r\nEND:VCARD\r\n";

        var contact = Assert.Single(await ReadFromString(src));

        Assert.Equal("one,two;three\\four\nfive", contact.CustomFields["X-TEST"]);
    }
}
