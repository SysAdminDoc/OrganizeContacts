using System.IO;
using System.Text;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

/// <summary>
/// Google Contacts CSV importer (Google's "Google CSV" export format).
/// Map covers the common fields; columns we don't recognise are preserved as X-* custom fields.
/// </summary>
public sealed class GoogleCsvImporter : IContactImporter
{
    public string Name => "Google CSV";
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".csv" };

    public bool CanRead(string path)
    {
        if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var fs = new StreamReader(path);
            var header = fs.ReadLine();
            return header is not null &&
                   (header.Contains("E-mail 1 - Value", StringComparison.OrdinalIgnoreCase) ||
                    header.Contains("Phone 1 - Value", StringComparison.OrdinalIgnoreCase) ||
                    header.Contains("Given Name", StringComparison.OrdinalIgnoreCase));
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
        var rowIdx = 0;
        foreach (var row in CsvReader.Read(reader))
        {
            ct.ThrowIfCancellationRequested();
            if (header is null)
            {
                header = row;
                continue;
            }
            rowIdx++;
            var c = MapRow(header, row, path);
            if (c is not null) yield return c;
        }
    }

    private static Contact? MapRow(List<string> header, List<string> row, string sourceFile)
    {
        var contact = new Contact { SourceFile = sourceFile, SourceFormat = "Google CSV" };
        var seen = false;

        string Get(string key) =>
            row.Count > IndexOf(header, key) && IndexOf(header, key) >= 0
                ? row[IndexOf(header, key)]
                : string.Empty;

        var first = Get("Given Name");
        var last = Get("Family Name");
        var middle = Get("Additional Name");
        var prefix = Get("Name Prefix");
        var suffix = Get("Name Suffix");
        var nick = Get("Nickname");

        if (!string.IsNullOrWhiteSpace(first)) contact.GivenName = first;
        if (!string.IsNullOrWhiteSpace(last)) contact.FamilyName = last;
        if (!string.IsNullOrWhiteSpace(middle)) contact.AdditionalNames = middle;
        if (!string.IsNullOrWhiteSpace(prefix)) contact.HonorificPrefix = prefix;
        if (!string.IsNullOrWhiteSpace(suffix)) contact.HonorificSuffix = suffix;
        if (!string.IsNullOrWhiteSpace(nick)) contact.Nickname = nick;

        var fn = Get("Name");
        if (string.IsNullOrWhiteSpace(fn))
            fn = string.Join(' ', new[] { first, middle, last }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (!string.IsNullOrWhiteSpace(fn))
        {
            contact.FormattedName = fn;
            seen = true;
        }

        var org = Get("Organization Name");
        var title = Get("Organization Title");
        if (!string.IsNullOrWhiteSpace(org)) { contact.Organization = org; seen = true; }
        if (!string.IsNullOrWhiteSpace(title)) contact.Title = title;

        var notes = Get("Notes");
        if (!string.IsNullOrWhiteSpace(notes)) contact.Notes = notes;

        var bday = Get("Birthday");
        if (DateOnly.TryParse(bday, out var bd)) contact.Birthday = bd;

        // Multi-row "E-mail N - Value" / "Phone N - Value" / "Address N - …"
        var emailValueCols = header
            .Select((h, i) => (h, i))
            .Where(t => t.h.StartsWith("E-mail ", StringComparison.OrdinalIgnoreCase) &&
                        t.h.EndsWith(" - Value", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var (h, i) in emailValueCols)
        {
            if (i >= row.Count) continue;
            var val = row[i];
            if (string.IsNullOrWhiteSpace(val)) continue;
            var typeCol = header.IndexOf(h.Replace(" - Value", " - Label"));
            var type = typeCol >= 0 && typeCol < row.Count ? row[typeCol] : "";
            foreach (var split in val.Split(new[] { " ::: ", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                contact.Emails.Add(new EmailAddress
                {
                    Address = split.Trim(),
                    Kind = ParseEmailKind(type),
                });
                seen = true;
            }
        }

        var phoneValueCols = header
            .Select((h, i) => (h, i))
            .Where(t => t.h.StartsWith("Phone ", StringComparison.OrdinalIgnoreCase) &&
                        t.h.EndsWith(" - Value", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var (h, i) in phoneValueCols)
        {
            if (i >= row.Count) continue;
            var val = row[i];
            if (string.IsNullOrWhiteSpace(val)) continue;
            var typeCol = header.IndexOf(h.Replace(" - Value", " - Label"));
            var type = typeCol >= 0 && typeCol < row.Count ? row[typeCol] : "";
            foreach (var split in val.Split(new[] { " ::: ", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                contact.Phones.Add(PhoneNumber.Parse(split.Trim(), ParsePhoneKind(type)));
                seen = true;
            }
        }

        // Addresses (Address 1 - Formatted / Street / City / Region / Postal / Country / Country Code)
        for (int n = 1; n <= 5; n++)
        {
            var street = Get($"Address {n} - Street");
            var city = Get($"Address {n} - City");
            var region = Get($"Address {n} - Region");
            var postal = Get($"Address {n} - Postal Code");
            var country = Get($"Address {n} - Country");
            var label = Get($"Address {n} - Label");
            if (string.IsNullOrWhiteSpace(street) && string.IsNullOrWhiteSpace(city) &&
                string.IsNullOrWhiteSpace(region) && string.IsNullOrWhiteSpace(postal) &&
                string.IsNullOrWhiteSpace(country)) continue;

            contact.Addresses.Add(new PostalAddress
            {
                Street = string.IsNullOrWhiteSpace(street) ? null : street,
                Locality = string.IsNullOrWhiteSpace(city) ? null : city,
                Region = string.IsNullOrWhiteSpace(region) ? null : region,
                PostalCode = string.IsNullOrWhiteSpace(postal) ? null : postal,
                Country = string.IsNullOrWhiteSpace(country) ? null : country,
                Kind = ParseAddressKind(label),
            });
        }

        var groupMembership = Get("Group Membership");
        if (!string.IsNullOrWhiteSpace(groupMembership))
            foreach (var group in groupMembership.Split(new[] { " ::: ", "\n", "," }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = group.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.Equals("* myContacts", StringComparison.OrdinalIgnoreCase))
                    contact.Categories.Add(trimmed);
            }

        var websiteValueCols = header
            .Select((h, i) => (h, i))
            .Where(t => t.h.StartsWith("Website ", StringComparison.OrdinalIgnoreCase) &&
                        t.h.EndsWith(" - Value", StringComparison.OrdinalIgnoreCase));
        foreach (var (_, i) in websiteValueCols)
        {
            if (i >= row.Count) continue;
            var val = row[i];
            if (!string.IsNullOrWhiteSpace(val)) contact.Urls.Add(val);
        }

        return seen ? contact : null;
    }

    private static int IndexOf(List<string> header, string key)
    {
        for (int i = 0; i < header.Count; i++)
            if (header[i].Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static EmailKind ParseEmailKind(string? type) => (type ?? "").Trim().ToUpperInvariant() switch
    {
        "WORK" => EmailKind.Work,
        "HOME" or "PERSONAL" => EmailKind.Personal,
        _ => EmailKind.Other,
    };

    private static PhoneKind ParsePhoneKind(string? type) => (type ?? "").Trim().ToUpperInvariant() switch
    {
        "MOBILE" or "CELL" => PhoneKind.Mobile,
        "HOME" => PhoneKind.Home,
        "WORK" => PhoneKind.Work,
        "FAX" or "WORK FAX" or "HOME FAX" => PhoneKind.Fax,
        "PAGER" => PhoneKind.Pager,
        "MAIN" => PhoneKind.Main,
        _ => PhoneKind.Other,
    };

    private static AddressKind ParseAddressKind(string? type) => (type ?? "").Trim().ToUpperInvariant() switch
    {
        "HOME" => AddressKind.Home,
        "WORK" => AddressKind.Work,
        _ => AddressKind.Other,
    };
}
