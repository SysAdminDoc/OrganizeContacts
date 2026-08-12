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

    private static async Task<Contact> ReadSingleAsync(IContactImporter importer, string path)
    {
        var contacts = new List<Contact>();
        await foreach (var contact in importer.ReadAsync(path)) contacts.Add(contact);
        return Assert.Single(contacts);
    }

    private static Contact RichContact()
    {
        var contact = new Contact
        {
            Uid = "csv-round-trip@example.test",
            Rev = "20260812T140000Z",
            FormattedName = "Dr. Ada M. Lovelace, PhD",
            GivenName = "Ada",
            AdditionalNames = "M.",
            FamilyName = "Lovelace",
            HonorificPrefix = "Dr.",
            HonorificSuffix = "PhD",
            Nickname = "Enchantress of Numbers",
            Organization = "Analytical Engines & Co.",
            Title = "Principal Programmer",
            Birthday = new DateOnly(1815, 12, 10),
            Anniversary = new DateOnly(1835, 7, 8),
            Notes = "Line one\nLine two, with commas",
            PhotoBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x01, 0x02 },
            PhotoMimeType = "image/png",
        };
        contact.Phones.Add(new PhoneNumber
        {
            Raw = "+44 (0)20 7946 0958 ext. 7",
            Digits = "44020794609587",
            E164 = "+442079460958",
            Kind = PhoneKind.Work,
            IsPreferred = true,
        });
        contact.Phones.Add(PhoneNumber.Parse("555-0102", PhoneKind.Mobile));
        contact.Phones.Add(PhoneNumber.Parse("555-0103", PhoneKind.Home));
        contact.Phones.Add(PhoneNumber.Parse("555-0104", PhoneKind.Work));
        contact.Emails.Add(new EmailAddress
        {
            Address = "Ada.Lovelace+lists@Example.test",
            CanonicalOverride = "ada.lovelace@example.test",
            Kind = EmailKind.Work,
            IsPreferred = true,
        });
        contact.Emails.Add(new EmailAddress { Address = "ada@home.example", Kind = EmailKind.Personal });
        contact.Emails.Add(new EmailAddress { Address = "ada@other.example", Kind = EmailKind.Other });
        contact.Emails.Add(new EmailAddress { Address = "ada@fourth.example", Kind = EmailKind.Other });
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
        contact.Addresses.Add(new PostalAddress
        {
            Street = "Second work address",
            Locality = "Oxford",
            Kind = AddressKind.Work,
        });
        contact.Addresses.Add(new PostalAddress
        {
            Street = "Home address",
            Locality = "London",
            Kind = AddressKind.Home,
        });
        contact.Categories.AddRange(new[] { "Friends, family", "Semi;Colon", "VIP" });
        contact.Urls.AddRange(new[] { "https://example.test/ada", "https://example.test/notes?q=1,2" });
        contact.CustomFields["X-ADA-NUMBER"] = "42; exact";
        return contact;
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
    public async Task Google_csv_round_trip_preserves_every_modeled_field()
    {
        var source = RichContact();
        var path = Path.Combine(Path.GetTempPath(), $"oc-google-exact-{Guid.NewGuid():N}.csv");
        try
        {
            await new GoogleCsvWriter().WriteFileAsync(path, new[] { source });
            var imported = await ReadSingleAsync(new GoogleCsvImporter(), path);

            var report = ExportReportComparer.Compare(new[] { source }, new[] { imported });
            Assert.True(report.IsExact, report.Summary + " " +
                string.Join(", ", report.Differences.Select(x => x.Field)));
            Assert.Equal(source.Phones[0].Raw, imported.Phones[0].Raw);
            Assert.Equal(source.Phones[0].E164, imported.Phones[0].E164);
            Assert.Equal(source.Emails[0].CanonicalOverride, imported.Emails[0].CanonicalOverride);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public async Task Outlook_csv_round_trip_preserves_every_modeled_field()
    {
        var source = RichContact();
        var path = Path.Combine(Path.GetTempPath(), $"oc-outlook-exact-{Guid.NewGuid():N}.csv");
        try
        {
            await new OutlookCsvWriter().WriteFileAsync(path, new[] { source });
            var imported = await ReadSingleAsync(new OutlookCsvImporter(), path);

            var report = ExportReportComparer.Compare(new[] { source }, new[] { imported });
            Assert.True(report.IsExact, report.Summary + " " +
                string.Join(", ", report.Differences.Select(x => x.Field)));
            Assert.Equal(source.Notes, imported.Notes);
            Assert.Equal(source.Phones[0].Raw, imported.Phones[0].Raw);
            Assert.Equal(source.Emails[0].CanonicalOverride, imported.Emails[0].CanonicalOverride);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public async Task Google_csv_ignores_stale_round_trip_metadata_after_visible_edit()
    {
        var source = RichContact();
        var path = Path.Combine(Path.GetTempPath(), $"oc-google-edited-{Guid.NewGuid():N}.csv");
        try
        {
            await new GoogleCsvWriter().WriteFileAsync(path, new[] { source });
            List<List<string>> rows;
            using (var reader = new StreamReader(path)) rows = CsvReader.Read(reader).ToList();
            var nameIndex = rows[0].FindIndex(x => x.Equals("Name", StringComparison.OrdinalIgnoreCase));
            rows[1][nameIndex] = "Edited in a spreadsheet";
            File.WriteAllText(path, string.Join(Environment.NewLine, rows.Select(CsvWriter.Format)) + Environment.NewLine);

            var imported = await ReadSingleAsync(new GoogleCsvImporter(), path);
            Assert.Equal("Edited in a spreadsheet", imported.FormattedName);
            Assert.Null(imported.Uid);
            Assert.Null(imported.PhotoBytes);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public async Task Csv_dialects_restore_unmodeled_columns_under_original_headers()
    {
        var cases = new[]
        {
            (Name: "google", Csv: "Given Name,Occupation,Favorite Color\nAda,Mathematician,Blue\n",
                Importer: (IContactImporter)new GoogleCsvImporter(), Writer: (Func<string, IReadOnlyList<Contact>, Task>)
                    ((path, contacts) => new GoogleCsvWriter().WriteFileAsync(path, contacts))),
            (Name: "outlook", Csv: "First Name,Department,Favorite Color\nGrace,Research,Navy\n",
                Importer: (IContactImporter)new OutlookCsvImporter(), Writer: (Func<string, IReadOnlyList<Contact>, Task>)
                    ((path, contacts) => new OutlookCsvWriter().WriteFileAsync(path, contacts))),
        };

        foreach (var item in cases)
        {
            var input = Tmp($"{item.Name}-unknown.csv", item.Csv);
            var output = Path.Combine(Path.GetTempPath(), $"oc-{item.Name}-unknown-out-{Guid.NewGuid():N}.csv");
            try
            {
                var imported = await ReadSingleAsync(item.Importer, input);
                await item.Writer(output, new[] { imported });

                List<List<string>> rows;
                using (var reader = new StreamReader(output)) rows = CsvReader.Read(reader).ToList();
                var expected = item.Name == "google" ? "Mathematician" : "Research";
                var knownHeader = item.Name == "google" ? "Occupation" : "Department";
                Assert.Equal(expected, rows[1][rows[0].FindIndex(x => x == knownHeader)]);
                Assert.Equal(item.Name == "google" ? "Blue" : "Navy",
                    rows[1][rows[0].FindIndex(x => x == "Favorite Color")]);

                var reimported = await ReadSingleAsync(item.Importer, output);
                Assert.True(ExportReportComparer.Compare(new[] { imported }, new[] { reimported }).IsExact);
            }
            finally
            {
                try { File.Delete(input); } catch { }
                try { File.Delete(output); } catch { }
            }
        }
    }

    [Fact]
    public async Task Outlook_csv_recovers_legacy_overflow_suffix()
    {
        var header = CsvWriter.Format(new[] { "First Name", "Business Phone", "Notes" });
        var row = CsvWriter.Format(new[]
        {
            "Legacy", "555-0100",
            "Original notes\n[OrganizeContacts overflow] phones: work=555-0101; mobile=555-0102 | " +
            "extra emails: one@example.test; two@example.test | " +
            "urls: https://one.example; https://two.example",
        });
        var path = Tmp("outlook-overflow.csv", header + "\n" + row + "\n");
        try
        {
            var imported = await ReadSingleAsync(new OutlookCsvImporter(), path);
            Assert.Equal("Original notes", imported.Notes);
            Assert.Equal(3, imported.Phones.Count);
            Assert.Equal(2, imported.Emails.Count);
            Assert.Equal(2, imported.Urls.Count);
            Assert.Contains(imported.Phones, x => x.Kind == PhoneKind.Mobile && x.Raw == "555-0102");
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
