using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

/// <summary>
/// Carries model fields that fixed Google/Outlook CSV schemas cannot represent. The
/// payload is guarded by a fingerprint of every visible CSV cell, so editing a visible
/// field makes the importer ignore stale hidden metadata instead of overwriting the edit.
/// Unknown source columns use reversible X-* keys and are restored to their original
/// headers when exported back to the same CSV dialect.
/// </summary>
internal static class CsvRoundTripMetadata
{
    public const string Header = "X-OrganizeContacts Round Trip";
    private const int CurrentVersion = 1;
    private const int MaxPayloadChars = 8 * 1024 * 1024;
    private const string PreservedColumnPrefix = "X-OC-CSV-";

    public static string Create(
        Contact contact,
        IReadOnlyList<string> visibleHeader,
        IReadOnlyList<string> visibleCells)
    {
        var payload = new Payload
        {
            Version = CurrentVersion,
            VisibleFingerprint = Fingerprint(visibleHeader, visibleCells),
            Uid = contact.Uid,
            Rev = contact.Rev,
            FormattedName = contact.FormattedName,
            GivenName = contact.GivenName,
            FamilyName = contact.FamilyName,
            AdditionalNames = contact.AdditionalNames,
            HonorificPrefix = contact.HonorificPrefix,
            HonorificSuffix = contact.HonorificSuffix,
            Nickname = contact.Nickname,
            Organization = contact.Organization,
            Title = contact.Title,
            Birthday = contact.Birthday,
            Anniversary = contact.Anniversary,
            Notes = contact.Notes,
            PhotoBytes = contact.PhotoBytes,
            PhotoMimeType = contact.PhotoMimeType,
            Phones = contact.Phones.Select(x => new PhoneData
            {
                Raw = x.Raw,
                Digits = x.Digits,
                E164 = x.E164,
                Kind = x.Kind,
                IsPreferred = x.IsPreferred,
            }).ToList(),
            Emails = contact.Emails.Select(x => new EmailData
            {
                Address = x.Address,
                CanonicalOverride = x.CanonicalOverride,
                Kind = x.Kind,
                IsPreferred = x.IsPreferred,
            }).ToList(),
            Addresses = contact.Addresses.Select(x => new AddressData
            {
                PoBox = x.PoBox,
                Extended = x.Extended,
                Street = x.Street,
                Locality = x.Locality,
                Region = x.Region,
                PostalCode = x.PostalCode,
                Country = x.Country,
                Kind = x.Kind,
                IsPreferred = x.IsPreferred,
            }).ToList(),
            Categories = contact.Categories.ToList(),
            Urls = contact.Urls.ToList(),
            CustomFields = new Dictionary<string, string>(contact.CustomFields, StringComparer.OrdinalIgnoreCase),
        };
        return JsonSerializer.Serialize(payload);
    }

    public static bool TryApply(Contact contact, IReadOnlyList<string> header, IReadOnlyList<string> row)
    {
        var metadataIndex = IndexOf(header, Header);
        if (metadataIndex < 0 || metadataIndex >= row.Count) return false;
        var raw = row[metadataIndex];
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxPayloadChars) return false;

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(raw);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return false;
        }

        if (payload is null || payload.Version != CurrentVersion ||
            payload.VisibleFingerprint?.Length != 64 ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(payload.VisibleFingerprint),
                Encoding.ASCII.GetBytes(Fingerprint(header, row, metadataIndex))))
            return false;

        contact.Uid = payload.Uid;
        contact.Rev = payload.Rev;
        contact.FormattedName = payload.FormattedName;
        contact.GivenName = payload.GivenName;
        contact.FamilyName = payload.FamilyName;
        contact.AdditionalNames = payload.AdditionalNames;
        contact.HonorificPrefix = payload.HonorificPrefix;
        contact.HonorificSuffix = payload.HonorificSuffix;
        contact.Nickname = payload.Nickname;
        contact.Organization = payload.Organization;
        contact.Title = payload.Title;
        contact.Birthday = payload.Birthday;
        contact.Anniversary = payload.Anniversary;
        contact.Notes = payload.Notes;
        contact.PhotoBytes = payload.PhotoBytes;
        contact.PhotoMimeType = payload.PhotoMimeType;

        contact.Phones.Clear();
        foreach (var phone in payload.Phones ?? Enumerable.Empty<PhoneData>())
        {
            contact.Phones.Add(new PhoneNumber
            {
                Raw = phone.Raw ?? string.Empty,
                Digits = phone.Digits ?? string.Empty,
                E164 = phone.E164,
                Kind = Enum.IsDefined(phone.Kind) ? phone.Kind : PhoneKind.Other,
                IsPreferred = phone.IsPreferred,
            });
        }

        contact.Emails.Clear();
        foreach (var email in payload.Emails ?? Enumerable.Empty<EmailData>())
        {
            contact.Emails.Add(new EmailAddress
            {
                Address = email.Address ?? string.Empty,
                CanonicalOverride = email.CanonicalOverride,
                Kind = Enum.IsDefined(email.Kind) ? email.Kind : EmailKind.Other,
                IsPreferred = email.IsPreferred,
            });
        }

        contact.Addresses.Clear();
        foreach (var address in payload.Addresses ?? Enumerable.Empty<AddressData>())
        {
            contact.Addresses.Add(new PostalAddress
            {
                PoBox = address.PoBox,
                Extended = address.Extended,
                Street = address.Street,
                Locality = address.Locality,
                Region = address.Region,
                PostalCode = address.PostalCode,
                Country = address.Country,
                Kind = Enum.IsDefined(address.Kind) ? address.Kind : AddressKind.Other,
                IsPreferred = address.IsPreferred,
            });
        }

        contact.Categories.Clear();
        contact.Categories.AddRange((payload.Categories ?? Enumerable.Empty<string?>()).OfType<string>());
        contact.Urls.Clear();
        contact.Urls.AddRange((payload.Urls ?? Enumerable.Empty<string?>()).OfType<string>());
        contact.CustomFields.Clear();
        if (payload.CustomFields is not null)
            foreach (var field in payload.CustomFields)
                if (!string.IsNullOrEmpty(field.Key)) contact.CustomFields[field.Key] = field.Value ?? string.Empty;
        return true;
    }

    public static bool HasModelData(Contact contact) =>
        contact.Uid is not null || contact.Rev is not null ||
        contact.FormattedName is not null || contact.GivenName is not null ||
        contact.FamilyName is not null || contact.AdditionalNames is not null ||
        contact.HonorificPrefix is not null || contact.HonorificSuffix is not null ||
        contact.Nickname is not null || contact.Organization is not null ||
        contact.Title is not null || contact.Birthday is not null ||
        contact.Anniversary is not null || contact.Notes is not null ||
        contact.PhotoBytes is not null || contact.PhotoMimeType is not null ||
        contact.Phones.Count > 0 || contact.Emails.Count > 0 ||
        contact.Addresses.Count > 0 || contact.Categories.Count > 0 ||
        contact.Urls.Count > 0 || contact.CustomFields.Count > 0;

    public static string PreserveColumn(string dialect, string header) =>
        $"{PreservedColumnPrefix}{dialect.ToUpperInvariant()}-{Convert.ToHexString(Encoding.UTF8.GetBytes(header))}";

    public static bool TryReadPreservedColumn(string key, string dialect, out string header)
    {
        var prefix = $"{PreservedColumnPrefix}{dialect.ToUpperInvariant()}-";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            header = string.Empty;
            return false;
        }

        try
        {
            header = Encoding.UTF8.GetString(Convert.FromHexString(key[prefix.Length..]));
            return !string.IsNullOrWhiteSpace(header);
        }
        catch (FormatException)
        {
            header = string.Empty;
            return false;
        }
    }

    public static IReadOnlyList<string> CollectPreservedColumns(
        IReadOnlyList<Contact> contacts,
        string dialect)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contact in contacts)
            foreach (var key in contact.CustomFields.Keys)
                if (TryReadPreservedColumn(key, dialect, out var header)) columns.Add(header);
        return columns.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void RestorePreservedColumns(
        Contact contact,
        string dialect,
        IReadOnlyList<string> header,
        List<string> row)
    {
        while (row.Count < header.Count) row.Add(string.Empty);
        foreach (var field in contact.CustomFields)
        {
            if (!TryReadPreservedColumn(field.Key, dialect, out var originalHeader)) continue;
            var index = IndexOf(header, originalHeader);
            if (index >= 0) row[index] = field.Value;
        }
    }

    private static int IndexOf(IReadOnlyList<string> values, string target)
    {
        for (var i = 0; i < values.Count; i++)
            if (values[i].Equals(target, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string Fingerprint(
        IReadOnlyList<string> header,
        IReadOnlyList<string> values,
        int excludedIndex = -1)
    {
        var canonical = new StringBuilder();
        AppendValues('H', header);
        AppendValues('R', values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));

        void AppendValues(char marker, IReadOnlyList<string> cells)
        {
            var includedCount = excludedIndex >= 0 && excludedIndex < cells.Count
                ? cells.Count - 1
                : cells.Count;
            canonical.Append(marker).Append(includedCount).Append(';');
            for (var i = 0; i < cells.Count; i++)
            {
                if (i == excludedIndex) continue;
                // Hash what CsvReader will return, including the writer's spreadsheet-formula
                // prefix. Sanitization is idempotent, so this is identical before and after
                // serialization even for phone numbers that begin with '+'.
                var value = CsvWriter.SanitizeCell(cells[i] ?? string.Empty);
                canonical.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(value)
                    .Append(';');
            }
        }
    }

    private sealed class Payload
    {
        public int Version { get; set; }
        public string? VisibleFingerprint { get; set; }
        public string? Uid { get; set; }
        public string? Rev { get; set; }
        public string? FormattedName { get; set; }
        public string? GivenName { get; set; }
        public string? FamilyName { get; set; }
        public string? AdditionalNames { get; set; }
        public string? HonorificPrefix { get; set; }
        public string? HonorificSuffix { get; set; }
        public string? Nickname { get; set; }
        public string? Organization { get; set; }
        public string? Title { get; set; }
        public DateOnly? Birthday { get; set; }
        public DateOnly? Anniversary { get; set; }
        public string? Notes { get; set; }
        public byte[]? PhotoBytes { get; set; }
        public string? PhotoMimeType { get; set; }
        public List<PhoneData> Phones { get; set; } = new();
        public List<EmailData> Emails { get; set; } = new();
        public List<AddressData> Addresses { get; set; } = new();
        public List<string> Categories { get; set; } = new();
        public List<string> Urls { get; set; } = new();
        public Dictionary<string, string> CustomFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PhoneData
    {
        public string? Raw { get; set; }
        public string? Digits { get; set; }
        public string? E164 { get; set; }
        public PhoneKind Kind { get; set; }
        public bool IsPreferred { get; set; }
    }

    private sealed class EmailData
    {
        public string? Address { get; set; }
        public string? CanonicalOverride { get; set; }
        public EmailKind Kind { get; set; }
        public bool IsPreferred { get; set; }
    }

    private sealed class AddressData
    {
        public string? PoBox { get; set; }
        public string? Extended { get; set; }
        public string? Street { get; set; }
        public string? Locality { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public AddressKind Kind { get; set; }
        public bool IsPreferred { get; set; }
    }
}
