using System.IO;
using System.Text;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

/// <summary>
/// Writes a Google Contacts-compatible CSV. Column ordering matches the schema Google
/// Contacts emits today; importers that ignore unknown columns will still parse it cleanly.
/// </summary>
public sealed class GoogleCsvWriter
{
    public async Task WriteFileAsync(string path, IReadOnlyList<Contact> contacts, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var w = new StreamWriter(fs, new UTF8Encoding(false));

        var maxEmails = Math.Max(1, contacts.Max(c => c.Emails.Count));
        var maxPhones = Math.Max(1, contacts.Max(c => c.Phones.Count));
        var maxAddresses = Math.Max(1, contacts.Max(c => c.Addresses.Count));

        var header = BuildHeader(maxEmails, maxPhones, maxAddresses);
        await w.WriteLineAsync(CsvWriter.Format(header));

        foreach (var c in contacts)
        {
            ct.ThrowIfCancellationRequested();
            await w.WriteLineAsync(CsvWriter.Format(BuildRow(c, maxEmails, maxPhones, maxAddresses)));
        }
    }

    private static List<string> BuildHeader(int emails, int phones, int addresses)
    {
        var h = new List<string>
        {
            "Name", "Given Name", "Additional Name", "Family Name", "Yomi Name",
            "Given Name Yomi", "Additional Name Yomi", "Family Name Yomi",
            "Name Prefix", "Name Suffix", "Initials", "Nickname", "Short Name",
            "Maiden Name", "Birthday", "Gender", "Location", "Billing Information",
            "Directory Server", "Mileage", "Occupation", "Hobby", "Sensitivity",
            "Priority", "Subject", "Notes", "Language", "Photo", "Group Membership",
            "Organization Name", "Organization Title",
        };
        for (int i = 1; i <= emails; i++) { h.Add($"E-mail {i} - Label"); h.Add($"E-mail {i} - Value"); }
        for (int i = 1; i <= phones; i++) { h.Add($"Phone {i} - Label"); h.Add($"Phone {i} - Value"); }
        for (int i = 1; i <= addresses; i++)
        {
            h.Add($"Address {i} - Label");
            h.Add($"Address {i} - Street");
            h.Add($"Address {i} - City");
            h.Add($"Address {i} - Region");
            h.Add($"Address {i} - Postal Code");
            h.Add($"Address {i} - Country");
        }
        h.Add("Website 1 - Label");
        h.Add("Website 1 - Value");
        return h;
    }

    private static List<string> BuildRow(Contact c, int emails, int phones, int addresses)
    {
        var row = new List<string>
        {
            c.FormattedName ?? string.Empty,
            c.GivenName ?? string.Empty,
            c.AdditionalNames ?? string.Empty,
            c.FamilyName ?? string.Empty,
            "", "", "", "",
            c.HonorificPrefix ?? string.Empty,
            c.HonorificSuffix ?? string.Empty,
            "", c.Nickname ?? string.Empty, "", "",
            c.Birthday?.ToString("yyyy-MM-dd") ?? string.Empty,
            "", "", "", "", "", "", "", "", "", "",
            c.Notes ?? string.Empty,
            "", "",
            c.Categories.Count > 0 ? string.Join(" ::: ", c.Categories) : string.Empty,
            c.Organization ?? string.Empty,
            c.Title ?? string.Empty,
        };
        for (int i = 0; i < emails; i++)
        {
            if (i < c.Emails.Count)
            {
                row.Add(c.Emails[i].Kind.ToString());
                row.Add(c.Emails[i].Address);
            }
            else { row.Add(""); row.Add(""); }
        }
        for (int i = 0; i < phones; i++)
        {
            if (i < c.Phones.Count)
            {
                row.Add(c.Phones[i].Kind.ToString());
                row.Add(string.IsNullOrEmpty(c.Phones[i].E164) ? c.Phones[i].Raw : c.Phones[i].E164!);
            }
            else { row.Add(""); row.Add(""); }
        }
        for (int i = 0; i < addresses; i++)
        {
            if (i < c.Addresses.Count)
            {
                var a = c.Addresses[i];
                row.Add(a.Kind.ToString());
                row.Add(a.Street ?? "");
                row.Add(a.Locality ?? "");
                row.Add(a.Region ?? "");
                row.Add(a.PostalCode ?? "");
                row.Add(a.Country ?? "");
            }
            else { for (int j = 0; j < 6; j++) row.Add(""); }
        }
        row.Add(c.Urls.Count > 0 ? "Other" : "");
        row.Add(c.Urls.FirstOrDefault() ?? "");
        return row;
    }
}
