using System.IO;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public class CsvImporterTests
{
    private static string Tmp(string name, string content)
    {
        var p = Path.Combine(Path.GetTempPath(), $"oc-csv-{Guid.NewGuid():N}-{name}");
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public async Task Google_csv_imports_basic_row()
    {
        var csv = "Name,Given Name,Family Name,Organization Name,E-mail 1 - Label,E-mail 1 - Value,Phone 1 - Label,Phone 1 - Value\n" +
                  "John Doe,John,Doe,Acme,Work,john@example.com,Mobile,5551234567\n";
        var path = Tmp("google.csv", csv);
        try
        {
            var importer = new GoogleCsvImporter();
            Assert.True(importer.CanRead(path));
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Equal("John Doe", list[0].FormattedName);
            Assert.Equal("Acme", list[0].Organization);
            Assert.Single(list[0].Emails);
            Assert.Equal("john@example.com", list[0].Emails[0].Address);
            Assert.Equal(EmailKind.Work, list[0].Emails[0].Kind);
            Assert.Single(list[0].Phones);
            Assert.Equal(PhoneKind.Mobile, list[0].Phones[0].Kind);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public async Task Outlook_csv_imports_basic_row()
    {
        var csv = "Title,First Name,Middle Name,Last Name,Suffix,Company,Department,Job Title,E-mail Address,Mobile Phone,Birthday,Notes\n" +
                  "Mr,John,A,Doe,Jr,Acme,Eng,Lead,john@example.com,555-1234,1985-04-21,Hello\n";
        var path = Tmp("outlook.csv", csv);
        try
        {
            var importer = new OutlookCsvImporter();
            Assert.True(importer.CanRead(path));
            var list = new List<Contact>();
            await foreach (var item in importer.ReadAsync(path)) list.Add(item);
            Assert.Single(list);
            var c = list[0];
            Assert.Equal("John A Doe", c.FormattedName);
            Assert.Equal("Acme", c.Organization);
            Assert.Equal("Lead", c.Title);
            Assert.Equal("Hello", c.Notes);
            Assert.Equal(new DateOnly(1985, 4, 21), c.Birthday);
            Assert.Single(c.Phones);
            Assert.Equal(PhoneKind.Mobile, c.Phones[0].Kind);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public async Task Google_csv_round_trip()
    {
        var src = new Contact
        {
            FormattedName = "Round Trip",
            GivenName = "Round",
            FamilyName = "Trip",
            Organization = "Acme",
            Notes = "Hello, World",
        };
        src.Emails.Add(new EmailAddress { Address = "rt@example.com", Kind = EmailKind.Work });
        src.Phones.Add(PhoneNumber.Parse("+15551234567", PhoneKind.Mobile));

        var path = Path.Combine(Path.GetTempPath(), $"oc-rt-{Guid.NewGuid():N}.csv");
        try
        {
            await new GoogleCsvWriter().WriteFileAsync(path, new[] { src });
            var importer = new GoogleCsvImporter();
            Assert.True(importer.CanRead(path));
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Equal("Round Trip", list[0].FormattedName);
            Assert.Equal("Acme", list[0].Organization);
            Assert.Single(list[0].Emails);
            Assert.Equal("rt@example.com", list[0].Emails[0].Address);
            Assert.Single(list[0].Phones);
            Assert.Contains("Hello, World", list[0].Notes);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public async Task Outlook_csv_round_trip()
    {
        var src = new Contact
        {
            FormattedName = "Out Look",
            GivenName = "Out",
            FamilyName = "Look",
            Organization = "Acme",
            Title = "Engineer",
        };
        src.Emails.Add(new EmailAddress { Address = "ol@example.com" });
        src.Phones.Add(PhoneNumber.Parse("5551234567", PhoneKind.Mobile));
        src.Phones.Add(PhoneNumber.Parse("+442071234567", PhoneKind.Work));

        var path = Path.Combine(Path.GetTempPath(), $"oc-rt-{Guid.NewGuid():N}.csv");
        try
        {
            await new OutlookCsvWriter().WriteFileAsync(path, new[] { src });
            var importer = new OutlookCsvImporter();
            Assert.True(importer.CanRead(path));
            var list = new List<Contact>();
            await foreach (var c in importer.ReadAsync(path)) list.Add(c);
            Assert.Single(list);
            Assert.Equal("Acme", list[0].Organization);
            Assert.Equal("Engineer", list[0].Title);
            Assert.Single(list[0].Emails);
            Assert.Equal(2, list[0].Phones.Count);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Csv_reader_handles_quoted_commas_and_newlines()
    {
        var csv = "a,b,c\n\"hello, world\",\"line1\nline2\",\"quote\"\"inside\"\n";
        using var sr = new StringReader(csv);
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)));
        var rows = CsvReader.Read(reader).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("hello, world", rows[1][0]);
        Assert.Equal("line1\nline2", rows[1][1]);
        Assert.Equal("quote\"inside", rows[1][2]);
    }
}
