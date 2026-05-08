using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Normalize;

namespace OrganizeContacts.Core.Merge;

public sealed class AutoMergeReport
{
    public List<MergePlan> Plans { get; } = new();
    public int Skipped { get; set; }
    public string Summary => $"{Plans.Count} merge plan(s); {Skipped} group(s) needed manual review.";
}

/// <summary>
/// Conservative auto-merge: a duplicate group is auto-mergeable only when every secondary
/// is an information subset of the primary (no field unique to the secondary)
/// AND the group's confidence is at or above the rules' AutoMergeThreshold.
/// </summary>
public sealed class AutoMergeService
{
    private readonly EmailCanonicalizer _email;

    public AutoMergeService(EmailCanonicalizer? email = null)
    {
        _email = email ?? EmailCanonicalizer.Default;
    }

    public AutoMergeReport Plan(IEnumerable<DuplicateGroup> groups, double threshold)
    {
        var report = new AutoMergeReport();
        foreach (var g in groups)
        {
            if (g.Members.Count < 2 || g.Confidence < threshold)
            {
                report.Skipped++;
                continue;
            }

            // Pick the richest record as the primary. Ties broken by most-recent update.
            var primary = g.Members
                .OrderByDescending(c => c.Phones.Count + c.Emails.Count + c.Addresses.Count + c.Urls.Count + c.Categories.Count)
                .ThenByDescending(c => Score(c))
                .ThenByDescending(c => c.UpdatedAt)
                .First();

            var secondaries = g.Members.Where(m => m.Id != primary.Id).ToList();

            if (!secondaries.All(s => IsSubsetOf(s, primary)))
            {
                report.Skipped++;
                continue;
            }

            // Build a plan that just keeps the primary's scalars; collections will be unioned by MergeEngine.
            var plan = new MergePlan
            {
                Primary = primary,
                Secondaries = secondaries,
                Choices = new List<MergeChoice>(),
                DeleteSecondaries = true,
            };
            report.Plans.Add(plan);
        }
        return report;
    }

    private static int Score(Contact c)
    {
        var s = 0;
        if (!string.IsNullOrWhiteSpace(c.FormattedName)) s++;
        if (!string.IsNullOrWhiteSpace(c.Organization)) s++;
        if (!string.IsNullOrWhiteSpace(c.Title)) s++;
        if (!string.IsNullOrWhiteSpace(c.Notes)) s++;
        if (c.Birthday.HasValue) s++;
        if (c.PhotoBytes is { Length: > 0 }) s++;
        return s;
    }

    /// <summary>True when every signal in 'sub' is also present in 'super'.</summary>
    private bool IsSubsetOf(Contact sub, Contact super)
    {
        // Phones: every sub phone must have a matching key in super.
        foreach (var p in sub.Phones)
        {
            var key = PhoneNormalizer.DedupeKey(p);
            if (!super.Phones.Any(sp => PhoneNormalizer.DedupeKey(sp) == key)) return false;
        }
        // Emails: by canonical form.
        foreach (var e in sub.Emails)
        {
            var key = _email.Canonicalize(e.Address);
            if (!super.Emails.Any(se => _email.Canonicalize(se.Address) == key)) return false;
        }
        // Scalar fields where the sub has a value the super lacks would block.
        if (HasUnique(sub.FormattedName, super.FormattedName)) return false;
        if (HasUnique(sub.GivenName, super.GivenName)) return false;
        if (HasUnique(sub.FamilyName, super.FamilyName)) return false;
        if (HasUnique(sub.Organization, super.Organization)) return false;
        if (HasUnique(sub.Title, super.Title)) return false;
        if (HasUnique(sub.Notes, super.Notes)) return false;
        // Don't auto-merge if sub has unique URLs the primary doesn't carry.
        if (sub.Urls.Any(u => !super.Urls.Contains(u, StringComparer.OrdinalIgnoreCase))) return false;
        return true;
    }

    private static bool HasUnique(string? sub, string? super)
    {
        if (string.IsNullOrWhiteSpace(sub)) return false;
        if (string.IsNullOrWhiteSpace(super)) return true;
        return !string.Equals(sub.Trim(), super.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
