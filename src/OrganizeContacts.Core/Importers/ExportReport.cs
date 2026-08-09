using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

public sealed record ExportFieldDifference(
    int ContactIndex,
    string ContactName,
    string Field,
    string? Before,
    string? After);

/// <summary>Field-level differences found by re-importing an exported contact file.</summary>
public sealed class ExportReport
{
    internal ExportReport(int inputContacts, int outputContacts)
    {
        InputContacts = inputContacts;
        OutputContacts = outputContacts;
    }

    public int InputContacts { get; }
    public int OutputContacts { get; }
    public int MatchedContacts => Math.Min(InputContacts, OutputContacts);
    public int DifferenceCount { get; internal set; }
    public List<ExportFieldDifference> Differences { get; } = new();
    public bool IsExact => InputContacts == OutputContacts && DifferenceCount == 0;
    public string Summary =>
        IsExact
            ? $"Round-trip exact for {InputContacts} contact(s)."
            : $"Round-trip: {OutputContacts}/{InputContacts} contact(s), {DifferenceCount} field difference(s).";
}

/// <summary>
/// Compares the fields represented by the contact model, ignoring generated IDs,
/// source metadata, and timestamps.
/// </summary>
public static class ExportReportComparer
{
    private const int MaxDetails = 100;

    public static ExportReport Compare(
        IReadOnlyList<Contact> input,
        IReadOnlyList<Contact> output)
    {
        var report = new ExportReport(input.Count, output.Count);
        var common = Math.Min(input.Count, output.Count);
        for (var i = 0; i < common; i++)
        {
            var before = input[i];
            var after = output[i];
            CompareScalar(report, i, before.DisplayName, after.DisplayName, "displayName", before.DisplayName);
            CompareScalar(report, i, before.GivenName, after.GivenName, "givenName", before.DisplayName);
            CompareScalar(report, i, before.FamilyName, after.FamilyName, "familyName", before.DisplayName);
            CompareScalar(report, i, before.AdditionalNames, after.AdditionalNames, "additionalNames", before.DisplayName);
            CompareScalar(report, i, before.HonorificPrefix, after.HonorificPrefix, "honorificPrefix", before.DisplayName);
            CompareScalar(report, i, before.HonorificSuffix, after.HonorificSuffix, "honorificSuffix", before.DisplayName);
            CompareScalar(report, i, before.Nickname, after.Nickname, "nickname", before.DisplayName);
            CompareScalar(report, i, before.Organization, after.Organization, "organization", before.DisplayName);
            CompareScalar(report, i, before.Title, after.Title, "title", before.DisplayName);
            CompareScalar(report, i, before.Birthday?.ToString("yyyy-MM-dd"), after.Birthday?.ToString("yyyy-MM-dd"), "birthday", before.DisplayName);
            CompareScalar(report, i, before.Anniversary?.ToString("yyyy-MM-dd"), after.Anniversary?.ToString("yyyy-MM-dd"), "anniversary", before.DisplayName);
            CompareScalar(report, i, before.Notes, after.Notes, "notes", before.DisplayName);
            CompareScalar(report, i, before.Uid, after.Uid, "uid", before.DisplayName);
            // Writers may add a current REV when the source did not carry one.
            // Treat that generated metadata as expected; compare explicit revisions.
            if (!string.IsNullOrWhiteSpace(before.Rev))
                CompareScalar(report, i, before.Rev, after.Rev, "rev", before.DisplayName);

            CompareScalar(report, i,
                string.Join("\u001F", before.Phones.Select(PhoneKey)),
                string.Join("\u001F", after.Phones.Select(PhoneKey)),
                "phones", before.DisplayName);
            CompareScalar(report, i,
                string.Join("\u001F", before.Emails.Select(EmailKey)),
                string.Join("\u001F", after.Emails.Select(EmailKey)),
                "emails", before.DisplayName);
            CompareScalar(report, i,
                string.Join("\u001F", before.Addresses.Select(AddressKey)),
                string.Join("\u001F", after.Addresses.Select(AddressKey)),
                "addresses", before.DisplayName);
            CompareScalar(report, i,
                string.Join("\u001F", before.Categories),
                string.Join("\u001F", after.Categories),
                "categories", before.DisplayName);
            CompareScalar(report, i,
                string.Join("\u001F", before.Urls),
                string.Join("\u001F", after.Urls),
                "urls", before.DisplayName);
            CompareScalar(report, i,
                string.Join("\u001F", before.CustomFields.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => $"{x.Key}={x.Value}")),
                string.Join("\u001F", after.CustomFields.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => $"{x.Key}={x.Value}")),
                "customFields", before.DisplayName);

            CompareScalar(report, i,
                before.PhotoBytes is null ? null : Convert.ToBase64String(before.PhotoBytes),
                after.PhotoBytes is null ? null : Convert.ToBase64String(after.PhotoBytes),
                "photoBytes", before.DisplayName);
            CompareScalar(report, i, before.PhotoMimeType, after.PhotoMimeType, "photoMimeType", before.DisplayName);
        }

        for (var i = common; i < input.Count; i++)
            AddDifference(report, new ExportFieldDifference(i, input[i].DisplayName, "contact", "present", null));
        for (var i = common; i < output.Count; i++)
            AddDifference(report, new ExportFieldDifference(i, output[i].DisplayName, "contact", null, "present"));

        return report;
    }

    private static void CompareScalar(
        ExportReport report,
        int index,
        string? before,
        string? after,
        string field,
        string contactName)
    {
        if (string.Equals(before, after, StringComparison.Ordinal)) return;
        AddDifference(report, new ExportFieldDifference(index, contactName, field, before, after));
    }

    private static void AddDifference(ExportReport report, ExportFieldDifference difference)
    {
        report.DifferenceCount++;
        if (report.Differences.Count < MaxDetails) report.Differences.Add(difference);
    }

    private static string PhoneKey(PhoneNumber phone) =>
        $"{phone.Digits}|{phone.Kind}|{phone.IsPreferred}";

    private static string EmailKey(EmailAddress email) =>
        $"{email.Address}|{email.Kind}|{email.IsPreferred}";

    private static string AddressKey(PostalAddress address) =>
        string.Join("|", address.PoBox, address.Extended, address.Street, address.Locality,
            address.Region, address.PostalCode, address.Country, address.Kind);
}
