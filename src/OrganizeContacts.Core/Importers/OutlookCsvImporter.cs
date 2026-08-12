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
    private const string OverflowMarker = "[OrganizeContacts overflow] ";

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

        // Use the same invariant-culture parser as the Google importer — Outlook for Windows
        // emits dates in the host culture, but we read those exports on every culture and
        // can't trust the current thread's locale.
        var bday = Get("Birthday");
        if (GoogleCsvImporter.TryParseCsvDate(bday, out var bd)) contact.Birthday = bd;
        var ann = Get("Anniversary");
        if (GoogleCsvImporter.TryParseCsvDate(ann, out var an)) contact.Anniversary = an;

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
        AddPhone(Get("Company Main Phone", "Main Phone"), PhoneKind.Main);

        void AddPhone(string raw, PhoneKind kind)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            contact.Phones.Add(PhoneNumber.Parse(raw.Trim(), kind));
            seen = true;
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
            seen = true;
        }

        var web = Get("Web Page", "Personal Web Page", "Business Web Page");
        if (!string.IsNullOrWhiteSpace(web))
        {
            contact.Urls.Add(web);
            seen = true;
        }

        var categories = Get("Categories");
        if (!string.IsNullOrWhiteSpace(categories))
            foreach (var c in categories.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                contact.Categories.Add(c.Trim());
                seen = true;
            }

        // OrganizeContacts versions before the metadata column stored values that did not
        // fit Outlook's fixed-width schema in a structured Notes suffix. Recover those
        // values before applying current metadata, which restores the original Notes text.
        if (RecoverOverflow(contact)) seen = true;

        // Keep every Outlook column the model does not understand. The matching writer
        // restores these values under their original headers (Department, Assistant's
        // Phone, custom Outlook fields, and future schema additions).
        for (var i = 0; i < header.Count && i < row.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(row[i]) || IsMappedHeader(header[i])) continue;
            contact.CustomFields[CsvRoundTripMetadata.PreserveColumn("OUTLOOK", header[i])] = row[i];
            seen = true;
        }

        if (CsvRoundTripMetadata.TryApply(contact, header, row)) seen = true;

        return seen || CsvRoundTripMetadata.HasModelData(contact) ? contact : null;
    }

    private static bool RecoverOverflow(Contact contact)
    {
        var notes = contact.Notes;
        if (string.IsNullOrEmpty(notes)) return false;
        var markerIndex = notes.LastIndexOf(OverflowMarker, StringComparison.Ordinal);
        if (markerIndex < 0 || (markerIndex > 0 && notes[markerIndex - 1] != '\n')) return false;

        var recovered = false;
        foreach (var section in notes[(markerIndex + OverflowMarker.Length)..]
                     .Split(" | ", StringSplitOptions.RemoveEmptyEntries))
        {
            if (section.StartsWith("phones: ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var item in section["phones: ".Length..]
                             .Split("; ", StringSplitOptions.RemoveEmptyEntries))
                {
                    var separator = item.IndexOf('=');
                    if (separator <= 0 || separator == item.Length - 1 ||
                        !Enum.TryParse<PhoneKind>(item[..separator], true, out var kind)) continue;
                    contact.Phones.Add(PhoneNumber.Parse(item[(separator + 1)..], kind));
                    recovered = true;
                }
            }
            else if (section.StartsWith("extra emails: ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var address in section["extra emails: ".Length..]
                             .Split("; ", StringSplitOptions.RemoveEmptyEntries))
                {
                    contact.Emails.Add(new EmailAddress { Address = address });
                    recovered = true;
                }
            }
            else if (section.StartsWith("urls: ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var url in section["urls: ".Length..]
                             .Split("; ", StringSplitOptions.RemoveEmptyEntries))
                {
                    contact.Urls.Add(url);
                    recovered = true;
                }
            }
        }

        if (!recovered) return false;
        var originalNotes = notes[..markerIndex];
        if (originalNotes.EndsWith('\n')) originalNotes = originalNotes[..^1];
        contact.Notes = string.IsNullOrEmpty(originalNotes) ? null : originalNotes;
        return true;
    }

    private static bool IsMappedHeader(string header)
    {
        if (header.Equals(CsvRoundTripMetadata.Header, StringComparison.OrdinalIgnoreCase)) return true;

        return header.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("First Name", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Given Name", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Middle Name", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Last Name", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Family Name", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Suffix", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Display Name", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Nickname", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Company", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Job Title", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Notes", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Birthday", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Anniversary", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("E-mail Address", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("E-mail 2 Address", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("E-mail 3 Address", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Mobile Phone", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Home Phone", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Home Phone 2", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Business Phone", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Business Phone 2", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Other Phone", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Pager", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Business Fax", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Home Fax", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Company Main Phone", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Main Phone", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Web Page", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Personal Web Page", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Business Web Page", StringComparison.OrdinalIgnoreCase) ||
               header.Equals("Categories", StringComparison.OrdinalIgnoreCase) ||
               IsAddressHeader(header);
    }

    private static bool IsAddressHeader(string header)
    {
        foreach (var prefix in new[] { "Home", "Business", "Other" })
        {
            if (header.Equals($"{prefix} Street", StringComparison.OrdinalIgnoreCase) ||
                header.Equals($"{prefix} City", StringComparison.OrdinalIgnoreCase) ||
                header.Equals($"{prefix} State", StringComparison.OrdinalIgnoreCase) ||
                header.Equals($"{prefix} Postal Code", StringComparison.OrdinalIgnoreCase) ||
                header.Equals($"{prefix} Country/Region", StringComparison.OrdinalIgnoreCase) ||
                header.Equals($"{prefix} Country", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static int IndexOf(List<string> header, string key)
    {
        for (int i = 0; i < header.Count; i++)
            if (header[i].Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
