using System.IO;
using System.Text;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

/// <summary>Writes an Outlook-for-Windows compatible CSV (English column names).</summary>
public sealed class OutlookCsvWriter
{
    private static readonly string[] Header = new[]
    {
        "Title", "First Name", "Middle Name", "Last Name", "Suffix", "Company",
        "Department", "Job Title",
        "Business Street", "Business City", "Business State", "Business Postal Code", "Business Country/Region",
        "Home Street", "Home City", "Home State", "Home Postal Code", "Home Country/Region",
        "Other Street", "Other City", "Other State", "Other Postal Code", "Other Country/Region",
        "Assistant's Phone", "Business Fax", "Business Phone", "Business Phone 2",
        "Callback", "Car Phone", "Company Main Phone",
        "Home Fax", "Home Phone", "Home Phone 2",
        "ISDN", "Mobile Phone", "Other Fax", "Other Phone", "Pager",
        "Primary Phone", "Radio Phone", "TTY/TDD Phone", "Telex",
        "E-mail Address", "E-mail 2 Address", "E-mail 3 Address",
        "Birthday", "Anniversary", "Web Page", "Notes", "Categories",
    };

    public async Task WriteFileAsync(string path, IReadOnlyList<Contact> contacts, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var w = new StreamWriter(fs, new UTF8Encoding(false));

        await w.WriteLineAsync(CsvWriter.Format(Header));
        foreach (var c in contacts)
        {
            ct.ThrowIfCancellationRequested();
            await w.WriteLineAsync(CsvWriter.Format(Row(c)));
        }
    }

    private static List<string> Row(Contact c)
    {
        var row = new List<string>
        {
            c.HonorificPrefix ?? "",
            c.GivenName ?? "",
            c.AdditionalNames ?? "",
            c.FamilyName ?? "",
            c.HonorificSuffix ?? "",
            c.Organization ?? "",
            "",                                  // Department
            c.Title ?? "",
        };

        var business = c.Addresses.FirstOrDefault(a => a.Kind == AddressKind.Work);
        var home = c.Addresses.FirstOrDefault(a => a.Kind == AddressKind.Home);
        var other = c.Addresses.FirstOrDefault(a => a.Kind == AddressKind.Other) ?? new PostalAddress();
        AppendAddress(row, business);
        AppendAddress(row, home);
        AppendAddress(row, other);

        // Phone block
        var workPhones = c.Phones.Where(p => p.Kind == PhoneKind.Work).ToList();
        var homePhones = c.Phones.Where(p => p.Kind == PhoneKind.Home).ToList();
        var pBusiness = workPhones.ElementAtOrDefault(0);
        var pBusiness2 = workPhones.ElementAtOrDefault(1);
        var pBusinessFax = c.Phones.FirstOrDefault(p => p.Kind == PhoneKind.Fax);
        var pHome = homePhones.ElementAtOrDefault(0);
        var pHome2 = homePhones.ElementAtOrDefault(1);
        var pMobile = c.Phones.FirstOrDefault(p => p.Kind == PhoneKind.Mobile);
        var pOther = c.Phones.FirstOrDefault(p => p.Kind == PhoneKind.Other);
        var pPager = c.Phones.FirstOrDefault(p => p.Kind == PhoneKind.Pager);
        var pMain = c.Phones.FirstOrDefault(p => p.Kind == PhoneKind.Main);

        row.Add("");                              // Assistant
        row.Add(P(pBusinessFax));
        row.Add(P(pBusiness));
        row.Add(P(pBusiness2));
        row.Add(""); row.Add(""); row.Add(P(pMain));
        row.Add(""); row.Add(P(pHome)); row.Add(P(pHome2));
        row.Add(""); row.Add(P(pMobile));
        row.Add(""); row.Add(P(pOther));
        row.Add(P(pPager));
        row.Add(""); row.Add(""); row.Add(""); row.Add("");

        // Emails
        row.Add(c.Emails.ElementAtOrDefault(0)?.Address ?? "");
        row.Add(c.Emails.ElementAtOrDefault(1)?.Address ?? "");
        row.Add(c.Emails.ElementAtOrDefault(2)?.Address ?? "");

        // Misc
        row.Add(c.Birthday?.ToString("yyyy-MM-dd") ?? "");
        row.Add(c.Anniversary?.ToString("yyyy-MM-dd") ?? "");
        row.Add(c.Urls.FirstOrDefault() ?? "");
        row.Add(c.Notes ?? "");
        row.Add(string.Join(';', c.Categories));

        return row;
    }

    private static void AppendAddress(List<string> row, PostalAddress? a)
    {
        if (a is null)
        {
            for (int i = 0; i < 5; i++) row.Add("");
            return;
        }
        row.Add(a.Street ?? "");
        row.Add(a.Locality ?? "");
        row.Add(a.Region ?? "");
        row.Add(a.PostalCode ?? "");
        row.Add(a.Country ?? "");
    }

    private static string P(PhoneNumber? p) =>
        p is null ? string.Empty : (string.IsNullOrEmpty(p.E164) ? p.Raw : p.E164!);
}
