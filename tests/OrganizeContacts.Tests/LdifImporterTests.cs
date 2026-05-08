using System.IO;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public class LdifImporterTests
{
    private static async Task<List<Contact>> Read(string ldif)
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-ldif-{Guid.NewGuid():N}.ldif");
        await File.WriteAllTextAsync(path, ldif);
        try
        {
            var importer = new LdifImporter();
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            return list;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task Reads_basic_mozilla_export()
    {
        var ldif = """
            dn: cn=John Doe,mail=john@example.com
            objectClass: top
            objectClass: person
            cn: John Doe
            givenName: John
            sn: Doe
            mail: john@example.com
            mozillaSecondEmail: j.doe@work.com
            cellPhone: 555-1234
            o: Acme

            """;
        var contacts = await Read(ldif);
        Assert.Single(contacts);
        var c = contacts[0];
        Assert.Equal("John Doe", c.FormattedName);
        Assert.Equal("John", c.GivenName);
        Assert.Equal("Doe", c.FamilyName);
        Assert.Equal("Acme", c.Organization);
        Assert.Equal(2, c.Emails.Count);
        Assert.Single(c.Phones);
        Assert.Equal(PhoneKind.Mobile, c.Phones[0].Kind);
    }

    [Fact]
    public async Task Reads_two_records_separated_by_blank_line()
    {
        var ldif = "dn: cn=A\ncn: A\nmail: a@x.com\n\ndn: cn=B\ncn: B\nmail: b@x.com\n\n";
        var contacts = await Read(ldif);
        Assert.Equal(2, contacts.Count);
    }
}
