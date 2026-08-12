using System.IO;
using System.Text.Json;
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

    [Fact]
    public async Task Round_trip_preserves_all_supported_fields_and_rfc_shapes()
    {
        var photo = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
        var source = new Contact
        {
            Uid = "urn:uuid:jcard-full",
            FormattedName = "Full Fidelity",
            GivenName = "Full",
            FamilyName = "Fidelity",
            Organization = "Acme, Inc.",
            Title = "Principal",
            Birthday = new DateOnly(1985, 4, 12),
            Anniversary = new DateOnly(2010, 6, 30),
            Notes = "Line one\nLine two",
            PhotoBytes = photo,
            PhotoMimeType = "image/png",
        };
        source.Phones.Add(PhoneNumber.Parse("+15551234567", PhoneKind.Mobile, preferred: true));
        source.Emails.Add(new EmailAddress
        {
            Address = "full@example.com",
            Kind = EmailKind.Work,
            IsPreferred = true,
        });
        source.Addresses.Add(new PostalAddress
        {
            PoBox = "PO 12",
            Extended = "Floor 3",
            Street = "123 Main Street",
            Locality = "Any Town",
            Region = "CA",
            PostalCode = "91921",
            Country = "US",
            Kind = AddressKind.Work,
            IsPreferred = true,
        });
        source.Urls.Add("https://example.com");
        source.Urls.Add("https://example.com/profile");
        source.Categories.Add("computers");
        source.Categories.Add("cameras");
        source.CustomFields["X-COMPLAINT-URI"] = "mailto:abuse@example.org";

        var path = Path.Combine(Path.GetTempPath(), $"oc-jc-full-{Guid.NewGuid():N}.jcard");
        try
        {
            await new JCardWriter().WriteFileAsync(path, new[] { source });

            using (var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path)))
            {
                var properties = json.RootElement[0][1];
                var categories = properties.EnumerateArray()
                    .Single(x => x[0].GetString() == "categories");
                Assert.Equal(5, categories.GetArrayLength());
                Assert.Equal("computers", categories[3].GetString());
                Assert.Equal("cameras", categories[4].GetString());

                var address = properties.EnumerateArray().Single(x => x[0].GetString() == "adr");
                Assert.Equal(JsonValueKind.Array, address[3].ValueKind);
                Assert.Equal("1", address[1].GetProperty("pref").GetString());
            }

            var imported = new List<Contact>();
            await foreach (var contact in new JCardImporter().ReadAsync(path)) imported.Add(contact);

            var result = Assert.Single(imported);
            Assert.Equal(2, result.Urls.Count);
            Assert.True(Assert.Single(result.Phones).IsPreferred);
            Assert.True(Assert.Single(result.Emails).IsPreferred);
            Assert.True(Assert.Single(result.Addresses).IsPreferred);
            Assert.Equal(photo, result.PhotoBytes);
            Assert.Equal("mailto:abuse@example.org", result.CustomFields["X-COMPLAINT-URI"]);
            Assert.True(ExportReportComparer.Compare(new[] { source }, imported).IsExact);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public async Task Reads_rfc_multivalues_nested_structures_and_custom_properties()
    {
        var json = """
            ["vcard", [
              ["version", {}, "text", "4.0"],
              ["fn", {}, "text", "Nested Values"],
              ["n", {}, "text", ["Values", "Nested", ["Alpha", "Beta"], "", ""]],
              ["categories", {}, "text", "one", "two"],
              ["x-karma-points", {}, "integer", 95]
            ]]
            """;
        var path = Path.Combine(Path.GetTempPath(), $"oc-jcard-nested-{Guid.NewGuid():N}.jcard");
        await File.WriteAllTextAsync(path, json);
        try
        {
            var contacts = new List<Contact>();
            await foreach (var imported in new JCardImporter().ReadAsync(path)) contacts.Add(imported);

            var contact = Assert.Single(contacts);
            Assert.Equal("Alpha,Beta", contact.AdditionalNames);
            Assert.Equal(new[] { "one", "two" }, contact.Categories);
            Assert.Equal("95", contact.CustomFields["X-KARMA-POINTS"]);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
