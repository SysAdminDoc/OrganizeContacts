using System.IO;
using System.Text;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

/// <summary>
/// Outlook Contacts CSV importer (English column names — Outlook for Windows export).
/// Handles the Outlook 2007/2010/2016/2021 schema variants.
/// </summary>
public sealed class OutlookCsvImporter : IContactImporter
{
    public string Name => "Outlook CSV";
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".csv" };

    public bool CanRead(string path)
    {
        if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var fs = new StreamReader(path);
            var header = fs.ReadLine();
            return header is not null &&
                   (header.Contains("E-mail Address", StringComparison.OrdinalIgnoreCase) ||
                    header.Contains("Business Phone", StringComparison.OrdinalIgnoreCase) ||
                    header.Contains("First Name", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public async IAsyncEnumerable<Contact> ReadAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        List<string>? header = null;
        foreach (var row in CsvReader.Read(reader))
        {
            ct.ThrowIfCancellationRequested();
            if (header is null) { header = row; continue; }
            var c = MapRow(header, row, path);
            if (c is not null) yield return c;
        }
    }

    private static Contact? MapRow(List<string> header, List<string> row, string sourceFile)
    {
        var contact = new Contact { SourceFile = sourceFile, SourceFormat = "Outlook CSV" };
        var seen = false;

        string Get(params string[] keys)
        {
            foreach (var k in keys)
            {
                var idx = IndexOf(header, k);
                if (idx >= 0 && idx < row.Count) return row[idx];
            }
            return string.Empty;
        }

        contact.GivenName = NullIfEmpty(Get("First Name", "Given Name"));
        contact.FamilyName = NullIfEmpty(Get("Last Name", "Family Name"));
        contact.AdditionalNames = NullIfEmpty(Get("Middle Name"));
        contact.HonorificPrefix = NullIfEmpty(Get("Title"));
        contact.HonorificSuffix = NullIfEmpty(Get("Suffix"));
        contact.Nickname = NullIfEmpty(Get("Nickname"));
        contact.Organization = NullIfEmpty(Get("Company"));
        contact.Title = NullIfEmpty(Get("Job Title"));
        contact.Notes = NullIfEmpty(Get("Notes"));

        var fn = NullIfEmpty(Get("Display Name"));
        if (string.IsNullOrEmpty(fn))
            fn = string.Join(' ', new[] { contact.GivenName, contact.AdditionalNames, contact.FamilyName }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (!string.IsNullOrWhiteSpace(fn))
        {
            contact.FormattedName = fn;
            seen = true;
        }
        if (!string.IsNullOrWhiteSpace(contact.Organization)) seen = true;

        var bday = Get("Birthday");
        if (DateOnly.TryParse(bday, out var bd)) contact.Birthday = bd;
        var ann = Get("Anniversary");
        if (DateOnly.TryParse(ann, out var an)) contact.Anniversary = an;

        // Up to three e-mail addresses in the standard Outlook schema.
        for (int n = 1; n <= 3; n++)
        {
            var addr = Get(n == 1 ? "E-mail Address" : $"E-mail {n} Address");
            if (!string.IsNullOrWhiteSpace(addr))
            {
                contact.Emails.Add(new EmailAddress { Address = addr.Trim(), Kind = EmailKind.Other });
                seen = true;
            }
        }

        // Phones
        AddPhone(Get("Mobile Phone"), PhoneKind.Mobile);
        AddPhone(Get("Home Phone"), PhoneKind.Home);
        AddPhone(Get("Home Phone 2"), PhoneKind.Home);
        AddPhone(Get("Business Phone"), PhoneKind.Work);
        AddPhone(Get("Business Phone 2"), PhoneKind.Work);
        AddPhone(Get("Other Phone"), PhoneKind.Other);
        AddPhone(Get("Pager"), PhoneKind.Pager);
        AddPhone(Get("Business Fax"), PhoneKind.Fax);
        AddPhone(Get("Home Fax"), PhoneKind.Fax);
        AddPhone(Get("Main Phone"), PhoneKind.Main);

        void AddPhone(string raw, PhoneKind kind)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            contact.Phones.Add(PhoneNumber.Parse(raw.Trim(), kind));
        }

        // Addresses — Home / Business / Other
        AddAddress("Home", AddressKind.Home);
        AddAddress("Business", AddressKind.Work);
        AddAddress("Other", AddressKind.Other);

        void AddAddress(string prefix, AddressKind kind)
        {
            var street = Get($"{prefix} Street");
            var city = Get($"{prefix} City");
            var state = Get($"{prefix} State");
            var postal = Get($"{prefix} Postal Code");
            var country = Get($"{prefix} Country/Region", $"{prefix} Country");
            if (string.IsNullOrWhiteSpace(street) && string.IsNullOrWhiteSpace(city) &&
                string.IsNullOrWhiteSpace(state) && string.IsNullOrWhiteSpace(postal) &&
                string.IsNullOrWhiteSpace(country)) return;
            contact.Addresses.Add(new PostalAddress
            {
                Street = NullIfEmpty(street),
                Locality = NullIfEmpty(city),
                Region = NullIfEmpty(state),
                PostalCode = NullIfEmpty(postal),
                Country = NullIfEmpty(country),
                Kind = kind,
            });
        }

        var web = Get("Web Page", "Personal Web Page", "Business Web Page");
        if (!string.IsNullOrWhiteSpace(web)) contact.Urls.Add(web);

        var categories = Get("Categories");
        if (!string.IsNullOrWhiteSpace(categories))
            foreach (var c in categories.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                contact.Categories.Add(c.Trim());

        return seen ? contact : null;
    }

    private static int IndexOf(List<string> header, string key)
    {
        for (int i = 0; i < header.Count; i++)
            if (header[i].Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
