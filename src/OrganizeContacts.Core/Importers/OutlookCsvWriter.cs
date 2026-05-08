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

        // Phone block — track which entries got written so we can append any leftovers
        // to Notes instead of silently dropping them. Outlook's schema is fixed-width
        // (2 Work, 2 Home, 1 of each Mobile/Other/Pager/Main, 1 Business Fax + 1 Home
        // Fax), so a contact with three work phones or two faxes used to lose data.
        var written = new HashSet<int>();
        var phones = c.Phones;

        PhoneNumber? PickKind(PhoneKind k, int skip = 0)
        {
            int seen = 0;
            for (int i = 0; i < phones.Count; i++)
            {
                if (written.Contains(i)) continue;
                if (phones[i].Kind != k) continue;
                if (seen++ < skip) continue;
                written.Add(i);
                return phones[i];
            }
            return null;
        }

        var pBusinessFax = PickKind(PhoneKind.Fax);   // first Fax → Business Fax
        var pBusiness    = PickKind(PhoneKind.Work);
        var pBusiness2   = PickKind(PhoneKind.Work);
        var pMain        = PickKind(PhoneKind.Main);
        var pHomeFax     = PickKind(PhoneKind.Fax);   // second Fax → Home Fax
        var pHome        = PickKind(PhoneKind.Home);
        var pHome2       = PickKind(PhoneKind.Home);
        var pMobile      = PickKind(PhoneKind.Mobile);
        var pOther       = PickKind(PhoneKind.Other);
        var pPager       = PickKind(PhoneKind.Pager);

        row.Add("");                              // Assistant's Phone
        row.Add(P(pBusinessFax));
        row.Add(P(pBusiness));
        row.Add(P(pBusiness2));
        row.Add(""); row.Add(""); row.Add(P(pMain));   // Callback, Car Phone, Company Main Phone
        row.Add(P(pHomeFax)); row.Add(P(pHome)); row.Add(P(pHome2));
        row.Add(""); row.Add(P(pMobile));
        row.Add(""); row.Add(P(pOther));
        row.Add(P(pPager));
        row.Add(""); row.Add(""); row.Add(""); row.Add("");

        // Emails — three slots; surplus goes into Notes too.
        row.Add(c.Emails.ElementAtOrDefault(0)?.Address ?? "");
        row.Add(c.Emails.ElementAtOrDefault(1)?.Address ?? "");
        row.Add(c.Emails.ElementAtOrDefault(2)?.Address ?? "");

        // Misc
        row.Add(c.Birthday?.ToString("yyyy-MM-dd") ?? "");
        row.Add(c.Anniversary?.ToString("yyyy-MM-dd") ?? "");
        row.Add(c.Urls.FirstOrDefault() ?? "");

        // Build the Notes column last so we can fold in any data the fixed-width Outlook
        // schema couldn't hold. Marker is grep-friendly so a follow-up Outlook export →
        // OrganizeContacts re-import can recover the surplus values.
        var leftoverPhones = new List<string>();
        for (int i = 0; i < phones.Count; i++)
            if (!written.Contains(i))
                leftoverPhones.Add($"{phones[i].Kind.ToString().ToLowerInvariant()}={P(phones[i])}");
        var leftoverEmails = c.Emails.Skip(3).Select(e => e.Address).ToList();
        var leftoverUrls   = c.Urls.Skip(1).ToList();

        var notes = c.Notes ?? string.Empty;
        var extras = new List<string>();
        if (leftoverPhones.Count > 0) extras.Add("phones: " + string.Join("; ", leftoverPhones));
        if (leftoverEmails.Count > 0) extras.Add("extra emails: " + string.Join("; ", leftoverEmails));
        if (leftoverUrls.Count > 0)   extras.Add("urls: "         + string.Join("; ", leftoverUrls));
        if (extras.Count > 0)
        {
            if (!string.IsNullOrEmpty(notes)) notes += "\n";
            notes += "[OrganizeContacts overflow] " + string.Join(" | ", extras);
        }
        row.Add(notes);
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
