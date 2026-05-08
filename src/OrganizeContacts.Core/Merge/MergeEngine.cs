using System.Globalization;
using System.Text.Json;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Merge;

public enum MergeFieldOrigin
{
    Primary,
    Secondary,
    Both,
    Custom,
}

/// <summary>
/// Per-field user choice.  ContactField indicates the conceptual field (FN, ORG, etc.)
/// and Value carries the resolved value (or list payload, when applicable).
/// </summary>
public sealed record MergeChoice(string Field, MergeFieldOrigin Origin, string? Value);

public sealed class MergePlan
{
    public Contact Primary { get; init; } = default!;
    public List<Contact> Secondaries { get; init; } = new();
    public List<MergeChoice> Choices { get; init; } = new();

    /// <summary>If true, secondaries are soft-deleted; if false, they remain alongside the merge.</summary>
    public bool DeleteSecondaries { get; init; } = true;
}

public sealed class MergeResult
{
    public Contact Survivor { get; init; } = default!;
    public List<Contact> RemovedSecondaries { get; init; } = new();
    public string ForwardJson { get; init; } = string.Empty;
    public string InverseJson { get; init; } = string.Empty;
}

/// <summary>
/// Applies a MergePlan into a survivor Contact, returning forward/inverse JSON for the undo journal.
/// Currently supports single-value scalar choices (FN, GivenName, FamilyName, Org, Title, Notes, Birthday)
/// and list-union for phones, emails, addresses, urls, categories, customFields.
/// </summary>
public sealed class MergeEngine
{
    public MergeResult Apply(MergePlan plan)
    {
        var before = Snapshot(plan);

        var s = Clone(plan.Primary);

        foreach (var choice in plan.Choices)
        {
            switch (choice.Field)
            {
                case "FormattedName": s.FormattedName = choice.Value; break;
                case "GivenName": s.GivenName = choice.Value; break;
                case "FamilyName": s.FamilyName = choice.Value; break;
                case "AdditionalNames": s.AdditionalNames = choice.Value; break;
                case "HonorificPrefix": s.HonorificPrefix = choice.Value; break;
                case "HonorificSuffix": s.HonorificSuffix = choice.Value; break;
                case "Nickname": s.Nickname = choice.Value; break;
                case "Organization": s.Organization = choice.Value; break;
                case "Title": s.Title = choice.Value; break;
                case "Notes": s.Notes = choice.Value; break;
                case "Birthday": s.Birthday = ParseDateOrNull(choice.Value); break;
                case "Anniversary": s.Anniversary = ParseDateOrNull(choice.Value); break;
            }
        }

        // Default policy for collections: union by canonical key.
        UnionPhones(s, plan.Secondaries);
        UnionEmails(s, plan.Secondaries);
        UnionAddresses(s, plan.Secondaries);
        UnionUrls(s, plan.Secondaries);
        UnionCategories(s, plan.Secondaries);
        foreach (var sec in plan.Secondaries)
            foreach (var kv in sec.CustomFields)
                if (!s.CustomFields.ContainsKey(kv.Key)) s.CustomFields[kv.Key] = kv.Value;

        // If the surviving primary has no photo, prefer the first secondary that has one.
        if (s.PhotoBytes is null || s.PhotoBytes.Length == 0)
        {
            var donor = plan.Secondaries.FirstOrDefault(x => x.PhotoBytes is { Length: > 0 });
            if (donor is not null)
            {
                s.PhotoBytes = donor.PhotoBytes;
                s.PhotoMimeType = donor.PhotoMimeType;
            }
        }

        s.UpdatedAt = DateTimeOffset.UtcNow;

        var after = JsonSerializer.Serialize(new
        {
            survivor = s,
            removed = plan.DeleteSecondaries ? plan.Secondaries.Select(x => x.Id) : Array.Empty<Guid>(),
        });

        return new MergeResult
        {
            Survivor = s,
            RemovedSecondaries = plan.DeleteSecondaries ? plan.Secondaries.ToList() : new List<Contact>(),
            ForwardJson = after,
            InverseJson = before,
        };
    }

    private static string Snapshot(MergePlan plan)
    {
        var bag = new
        {
            primary = plan.Primary,
            secondaries = plan.Secondaries,
        };
        return JsonSerializer.Serialize(bag);
    }

    private static Contact Clone(Contact src)
    {
        var json = JsonSerializer.Serialize(src);
        return JsonSerializer.Deserialize<Contact>(json)!;
    }

    /// <summary>Invariant-culture parse so a user choosing the date "07/05/2026"
    /// from a non-US-locale dialog doesn't crash the merge.</summary>
    private static DateOnly? ParseDateOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[] { "yyyy-MM-dd", "yyyyMMdd", "yyyy-MM-ddTHH:mm:ssZ", "yyyy/MM/dd" };
        if (DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
            return d2;
        // Last-ditch: parse as DateTime (handles ISO-8601 timestamps) and discard time.
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    private static void UnionPhones(Contact survivor, List<Contact> secs)
    {
        var existing = new HashSet<string>(survivor.Phones.Select(KeyForPhone), StringComparer.OrdinalIgnoreCase);
        foreach (var sec in secs)
            foreach (var p in sec.Phones)
                if (existing.Add(KeyForPhone(p)))
                    survivor.Phones.Add(p);
    }

    private static string KeyForPhone(PhoneNumber p) =>
        !string.IsNullOrEmpty(p.E164) ? p.E164!
        : p.Digits.Length >= 7 ? p.Digits[^7..]
        : p.Raw;

    private static void UnionEmails(Contact survivor, List<Contact> secs)
    {
        var existing = new HashSet<string>(survivor.Emails.Select(e => e.Canonical), StringComparer.OrdinalIgnoreCase);
        foreach (var sec in secs)
            foreach (var e in sec.Emails)
                if (existing.Add(e.Canonical))
                    survivor.Emails.Add(e);
    }

    private static void UnionAddresses(Contact survivor, List<Contact> secs)
    {
        var existing = new HashSet<string>(survivor.Addresses.Select(a => a.OneLine), StringComparer.OrdinalIgnoreCase);
        foreach (var sec in secs)
            foreach (var a in sec.Addresses)
                if (existing.Add(a.OneLine))
                    survivor.Addresses.Add(a);
    }

    private static void UnionUrls(Contact survivor, List<Contact> secs)
    {
        var existing = new HashSet<string>(survivor.Urls, StringComparer.OrdinalIgnoreCase);
        foreach (var sec in secs)
            foreach (var u in sec.Urls)
                if (existing.Add(u))
                    survivor.Urls.Add(u);
    }

    private static void UnionCategories(Contact survivor, List<Contact> secs)
    {
        var existing = new HashSet<string>(survivor.Categories, StringComparer.OrdinalIgnoreCase);
        foreach (var sec in secs)
            foreach (var c in sec.Categories)
                if (existing.Add(c))
                    survivor.Categories.Add(c);
    }
}
