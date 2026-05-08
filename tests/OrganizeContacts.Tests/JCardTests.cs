using System.IO;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public class JCardTests
{
    [Fact]
    public async Task Reads_single_jcard()
    {
        var json = """
            ["vcard", [
              ["version", {}, "text", "4.0"],
              ["fn", {}, "text", "John Doe"],
              ["n", {}, "text", ["Doe", "John", "", "", ""]],
              ["email", {"type":"work"}, "text", "john@example.com"],
              ["tel", {"type":"cell"}, "uri", "+15551234567"]
            ]]
            """;
        var path = Path.Combine(Path.GetTempPath(), $"oc-jcard-{Guid.NewGuid():N}.jcard");
        await File.WriteAllTextAsync(path, json);
        try
        {
            var importer = new JCardImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Equal("John Doe", list[0].FormattedName);
            Assert.Equal("Doe", list[0].FamilyName);
            Assert.Equal("John", list[0].GivenName);
            Assert.Single(list[0].Emails);
            Assert.Equal(EmailKind.Work, list[0].Emails[0].Kind);
            Assert.Single(list[0].Phones);
            Assert.Equal(PhoneKind.Mobile, list[0].Phones[0].Kind);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public async Task Round_trips_through_writer()
    {
        var c = new Contact
        {
            FormattedName = "Round Trip",
            GivenName = "Round",
            FamilyName = "Trip",
            Organization = "Acme",
        };
        c.Emails.Add(new EmailAddress { Address = "rt@example.com", Kind = EmailKind.Work });
        c.Phones.Add(PhoneNumber.Parse("+15551234567", PhoneKind.Mobile));

        var path = Path.Combine(Path.GetTempPath(), $"oc-jc-rt-{Guid.NewGuid():N}.jcard");
        try
        {
            await new JCardWriter().WriteFileAsync(path, new[] { c });
            var read = new List<Contact>();
            await foreach (var x in new JCardImporter().ReadAsync(path)) read.Add(x);
            Assert.Single(read);
            Assert.Equal("Round Trip", read[0].FormattedName);
            Assert.Equal("Acme", read[0].Organization);
            Assert.Single(read[0].Emails);
            Assert.Single(read[0].Phones);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
