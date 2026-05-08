using System.IO;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public class VCardWriterTests
{
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
        Assert.Contains("TEL;TYPE=MOBILE:+15551234567", output);
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
}
