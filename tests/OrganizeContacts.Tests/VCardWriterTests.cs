using System.IO;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public class VCardWriterTests
{
    private static Contact RichContact()
    {
        var contact = new Contact
        {
            Uid = "urn:uuid:vcard-rich",
            Rev = "20260812T150000Z",
            FormattedName = "Dr. Ada M. Lovelace, PhD",
            GivenName = "Ada",
            AdditionalNames = "M.",
            FamilyName = "Lovelace",
            HonorificPrefix = "Dr.",
            HonorificSuffix = "PhD",
            Nickname = "Enchantress",
            Organization = "Analytical Engines; Labs",
            Title = "Principal Programmer",
            Birthday = new DateOnly(1815, 12, 10),
            Anniversary = new DateOnly(1835, 7, 8),
            Notes = "Line one\nLine two, with punctuation; and a slash \\",
            PhotoBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x01, 0x02 },
            PhotoMimeType = "image/png",
        };
        contact.Phones.Add(PhoneNumber.Parse("+1-202-555-0100;ext=7", PhoneKind.Work, preferred: true));
        contact.Phones.Add(PhoneNumber.Parse("555-0101", PhoneKind.Fax));
        contact.Phones.Add(PhoneNumber.Parse("555-0102", PhoneKind.Main));
        contact.Emails.Add(new EmailAddress
        {
            Address = "ada@home.example",
            Kind = EmailKind.Personal,
            IsPreferred = true,
        });
        contact.Emails.Add(new EmailAddress { Address = "ada@work.example", Kind = EmailKind.Work });
        contact.Addresses.Add(new PostalAddress
        {
            PoBox = "PO 42",
            Extended = "Engine Room",
            Street = "12 St James's Square",
            Locality = "London",
            Region = "Greater London",
            PostalCode = "SW1Y 4LB",
            Country = "United Kingdom",
            Kind = AddressKind.Work,
            IsPreferred = true,
        });
        contact.Categories.AddRange(new[] { "Friends, family", "Semi;Colon", "VIP" });
        contact.Urls.Add("https://e.test/a,b;c");
        contact.CustomFields["X-ESCAPED"] = "one,two;three\\four\nfive";
        return contact;
    }

    [Fact]
    public void Writes_basic_card_in_3_0()
    {
        var c = new Contact
        {
            FormattedName = "John Doe",
            GivenName = "John",
            FamilyName = "Doe",
        };
        c.Phones.Add(PhoneNumber.Parse("+15551234567", PhoneKind.Mobile));
        c.Emails.Add(new EmailAddress { Address = "john@example.com", Kind = EmailKind.Work });

        var w = new VCardWriter();
        var output = w.Write(c);

        Assert.Contains("BEGIN:VCARD", output);
        Assert.Contains("VERSION:3.0", output);
        Assert.Contains("FN:John Doe", output);
        Assert.Contains("N:Doe;John;;;", output);
        Assert.Contains("TEL;TYPE=CELL:+15551234567", output);
        Assert.Contains("EMAIL;TYPE=WORK:john@example.com", output);
        Assert.Contains("END:VCARD", output);
    }

    [Fact]
    public async Task Round_trips_through_disk()
    {
        var src = new Contact
        {
            FormattedName = "Round Trip",
            GivenName = "Round",
            FamilyName = "Trip",
            Notes = "line1\nline2",
            Organization = "Acme; Inc",
        };
        src.Phones.Add(PhoneNumber.Parse("5551234567"));
        src.Emails.Add(new EmailAddress { Address = "rt@example.com" });
        src.CustomFields["X-CUSTOM"] = "value";

        var path = Path.Combine(Path.GetTempPath(), $"oc-rt-{Guid.NewGuid():N}.vcf");
        try
        {
            await new VCardWriter().WriteFileAsync(path, new[] { src });
            var importer = new VCardImporter();
            var read = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) read.Add(c);

            Assert.Single(read);
            var dst = read[0];
            Assert.Equal("Round Trip", dst.FormattedName);
            Assert.Equal("Round", dst.GivenName);
            Assert.Equal("Trip", dst.FamilyName);
            Assert.Equal("line1\nline2", dst.Notes);
            Assert.Equal("Acme; Inc", dst.Organization);
            Assert.Single(dst.Phones);
            Assert.Single(dst.Emails);
            Assert.Equal("value", dst.CustomFields["X-CUSTOM"]);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Round_trips_android_style_base64_photo()
    {
        var photo = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03 };
        var input = $"""
            BEGIN:VCARD
            VERSION:2.1
            FN:Android Contact
            PHOTO;ENCODING=BASE64;TYPE=JPEG:{Convert.ToBase64String(photo)}
            END:VCARD
            """;

        var importer = new VCardImporter();
        var parsed = Assert.Single(importer.ParseAll(input));
        var output = new VCardWriter().Write(parsed);
        var roundTripped = Assert.Single(importer.ParseAll(output));

        Assert.Equal(photo, roundTripped.PhotoBytes);
        Assert.Equal("image/jpeg", roundTripped.PhotoMimeType);
    }

    [Fact]
    public void Folds_long_lines_to_75_octets()
    {
        var c = new Contact
        {
            FormattedName = new string('a', 200),
        };
        var output = new VCardWriter().Write(c);
        var fnLine = output.Split("\r\n").FirstOrDefault(l => l.StartsWith("FN:"));
        Assert.NotNull(fnLine);
        Assert.True(fnLine!.Length <= 75, $"Line was {fnLine.Length} octets: {fnLine}");
    }

    [Theory]
    [InlineData(VCardVersion.V3_0)]
    [InlineData(VCardVersion.V4_0)]
    public void Preserves_every_modeled_vcard_field(VCardVersion version)
    {
        var source = RichContact();
        var writer = new VCardWriter { Version = version };

        var serialized = writer.Write(source);
        var imported = Assert.Single(new VCardImporter().ParseAll(serialized));
        var report = ExportReportComparer.Compare(new[] { source }, new[] { imported });

        Assert.True(report.IsExact, report.Summary + " " +
            string.Join(", ", report.Differences.Select(x => x.Field)));
        Assert.Equal(source.Phones[0].Raw, imported.Phones[0].Raw);
        Assert.Equal(source.CustomFields["X-ESCAPED"], imported.CustomFields["X-ESCAPED"]);
        if (version == VCardVersion.V4_0)
        {
            Assert.Contains("TEL;VALUE=uri;TYPE=WORK;PREF=1:tel:+1-202-555-0100;ext=7", serialized);
            Assert.Contains("EMAIL;TYPE=HOME;PREF=1:ada@home.example", serialized);
            Assert.Contains("ADR;TYPE=WORK;PREF=1:", serialized);
            Assert.DoesNotContain("TYPE=PREF", serialized);
            Assert.Contains("ANNIVERSARY:1835-07-08", serialized);
        }
        else
        {
            Assert.Contains("TEL;TYPE=WORK,PREF:+1-202-555-0100\\;ext=7", serialized);
            Assert.Contains("X-ANNIVERSARY:1835-07-08", serialized);
        }
        Assert.Contains("URL:https://e.test/a,b;c", serialized);
    }

    [Fact]
    public void Preserves_external_photo_uri_without_fetching_it()
    {
        const string input = "BEGIN:VCARD\r\nVERSION:4.0\r\n" +
                             "PHOTO:https://images.example.test/a,b;c.jpg\r\nEND:VCARD\r\n";
        var importer = new VCardImporter();

        var source = Assert.Single(importer.ParseAll(input));
        var serialized = new VCardWriter { Version = VCardVersion.V4_0 }.Write(source);
        var roundTripped = Assert.Single(importer.ParseAll(serialized));

        Assert.Equal("https://images.example.test/a,b;c.jpg",
            source.CustomFields[VCardImporter.PreservedPhotoUriField]);
        Assert.Contains("PHOTO:https://images.example.test/a,b;c.jpg", serialized);
        Assert.DoesNotContain("X-ORGANIZECONTACTS-PHOTO-URI:", serialized);
        Assert.True(ExportReportComparer.Compare(new[] { source }, new[] { roundTripped }).IsExact);
    }

    [Fact]
    public void Writer_emits_required_name_properties_for_unnamed_contacts()
    {
        var output = new VCardWriter { Version = VCardVersion.V4_0 }.Write(new Contact());

        Assert.Contains("N:;;;;\r\n", output);
        Assert.Contains("FN:\r\n", output);
        Assert.Single(new VCardImporter().ParseAll(output));
    }
}
