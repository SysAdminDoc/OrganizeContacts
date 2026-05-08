using System.Text.RegularExpressions;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Normalize;

namespace OrganizeContacts.Core.Cleanup;

public sealed class BatchCleanupReport
{
    public int PhonesDeduped { get; set; }
    public int EmailsDeduped { get; set; }
    public int UrlsDeduped { get; set; }
    public int CategoriesDeduped { get; set; }
    public int PhonesNormalized { get; set; }
    public int EmailsCanonicalized { get; set; }
    public int RegexHits { get; set; }
    public int ContactsTouched { get; set; }

    public string Summary =>
        $"Touched {ContactsTouched} contact(s): " +
        $"phones {PhonesDeduped}/{PhonesNormalized}, " +
        $"emails {EmailsDeduped}/{EmailsCanonicalized}, " +
        $"urls {UrlsDeduped}, " +
        $"categories {CategoriesDeduped}, " +
        $"regex hits {RegexHits}.";
}

public enum RegexTarget
{
    FormattedName,
    GivenName,
    FamilyName,
    Organization,
    Title,
    Notes,
    EmailAddress,
    PhoneRaw,
}

public sealed record RegexEdit(RegexTarget Target, string Pattern, string Replacement, RegexOptions Options = RegexOptions.None);

/// <summary>
/// Field-level cleanup that applies to one or more contacts in-place.
/// Deduplication uses canonical keys (E.164/last7 for phones, EmailCanonicalizer for emails).
/// </summary>
public sealed class BatchCleanup
{
    private readonly PhoneNormalizer? _phone;
    private readonly EmailCanonicalizer _email;

    public BatchCleanup(PhoneNormalizer? phone = null, EmailCanonicalizer? email = null)
    {
        _phone = phone;
        _email = email ?? EmailCanonicalizer.Default;
    }

    public BatchCleanupReport Run(
        IEnumerable<Contact> contacts,
        bool dedupePhones = true,
        bool dedupeEmails = true,
        bool dedupeUrls = true,
        bool dedupeCategories = true,
        bool normalizePhones = true,
        bool canonicalizeEmails = true,
        IReadOnlyList<RegexEdit>? regexEdits = null)
    {
        var report = new BatchCleanupReport();
        foreach (var c in contacts)
        {
            var touched = false;
            if (normalizePhones && _phone is not null)
            {
                var before = c.Phones.Count(p => !string.IsNullOrEmpty(p.E164));
                _phone.Normalize(c);
                var after = c.Phones.Count(p => !string.IsNullOrEmpty(p.E164));
                if (after > before) { report.PhonesNormalized += after - before; touched = true; }
            }

            if (canonicalizeEmails)
            {
                var before = c.Emails.Count(e => !string.IsNullOrEmpty(e.CanonicalOverride));
                _email.Apply(c);
                var after = c.Emails.Count(e => !string.IsNullOrEmpty(e.CanonicalOverride));
                if (after > before) { report.EmailsCanonicalized += after - before; touched = true; }
            }

            if (dedupePhones)
            {
                var removed = DedupeBy(c.Phones, p => PhoneNormalizer.DedupeKey(p));
                if (removed > 0) { report.PhonesDeduped += removed; touched = true; }
            }
            if (dedupeEmails)
            {
                var removed = DedupeBy(c.Emails, e => _email.Canonicalize(e.Address));
                if (removed > 0) { report.EmailsDeduped += removed; touched = true; }
            }
            if (dedupeUrls)
            {
                var removed = DedupeStringList(c.Urls, StringComparer.OrdinalIgnoreCase);
                if (removed > 0) { report.UrlsDeduped += removed; touched = true; }
            }
            if (dedupeCategories)
            {
                var removed = DedupeStringList(c.Categories, StringComparer.OrdinalIgnoreCase);
                if (removed > 0) { report.CategoriesDeduped += removed; touched = true; }
            }

            if (regexEdits is { Count: > 0 })
            {
                foreach (var edit in regexEdits)
                {
                    var n = ApplyRegex(c, edit);
                    if (n > 0) { report.RegexHits += n; touched = true; }
                }
            }

            if (touched)
            {
                c.UpdatedAt = DateTimeOffset.UtcNow;
                report.ContactsTouched++;
            }
        }
        return report;
    }

    private static int DedupeBy<T>(List<T> list, Func<T, string> keyFn)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var key = keyFn(list[i]);
            if (string.IsNullOrEmpty(key)) continue;
            if (!seen.Add(key))
            {
                list.RemoveAt(i);
                removed++;
            }
        }
        list.Reverse();
        list.Reverse();
        return removed;
    }

    private static int DedupeStringList(List<string> list, StringComparer cmp)
    {
        var seen = new HashSet<string>(cmp);
        var removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!seen.Add(list[i])) { list.RemoveAt(i); removed++; }
        }
        return removed;
    }

    private static int ApplyRegex(Contact c, RegexEdit edit)
    {
        var rgx = new Regex(edit.Pattern, edit.Options);
        var hits = 0;
        switch (edit.Target)
        {
            case RegexTarget.FormattedName:
                if (c.FormattedName is not null && rgx.IsMatch(c.FormattedName))
                { c.FormattedName = rgx.Replace(c.FormattedName, edit.Replacement); hits++; }
                break;
            case RegexTarget.GivenName:
                if (c.GivenName is not null && rgx.IsMatch(c.GivenName))
                { c.GivenName = rgx.Replace(c.GivenName, edit.Replacement); hits++; }
                break;
            case RegexTarget.FamilyName:
                if (c.FamilyName is not null && rgx.IsMatch(c.FamilyName))
                { c.FamilyName = rgx.Replace(c.FamilyName, edit.Replacement); hits++; }
                break;
            case RegexTarget.Organization:
                if (c.Organization is not null && rgx.IsMatch(c.Organization))
                { c.Organization = rgx.Replace(c.Organization, edit.Replacement); hits++; }
                break;
            case RegexTarget.Title:
                if (c.Title is not null && rgx.IsMatch(c.Title))
                { c.Title = rgx.Replace(c.Title, edit.Replacement); hits++; }
                break;
            case RegexTarget.Notes:
                if (c.Notes is not null && rgx.IsMatch(c.Notes))
                { c.Notes = rgx.Replace(c.Notes, edit.Replacement); hits++; }
                break;
            case RegexTarget.EmailAddress:
                for (int i = 0; i < c.Emails.Count; i++)
                {
                    if (rgx.IsMatch(c.Emails[i].Address))
                    {
                        var e = c.Emails[i];
                        c.Emails[i] = new EmailAddress
                        {
                            Address = rgx.Replace(e.Address, edit.Replacement),
                            CanonicalOverride = e.CanonicalOverride,
                            Kind = e.Kind,
                            IsPreferred = e.IsPreferred,
                            SourceId = e.SourceId,
                        };
                        hits++;
                    }
                }
                break;
            case RegexTarget.PhoneRaw:
                for (int i = 0; i < c.Phones.Count; i++)
                {
                    if (rgx.IsMatch(c.Phones[i].Raw))
                    {
                        var p = c.Phones[i];
                        var newRaw = rgx.Replace(p.Raw, edit.Replacement);
                        c.Phones[i] = new PhoneNumber
                        {
                            Raw = newRaw,
                            Digits = new string(newRaw.Where(char.IsDigit).ToArray()),
                            E164 = null, // re-normalize next pass
                            Kind = p.Kind,
                            IsPreferred = p.IsPreferred,
                            SourceId = p.SourceId,
                        };
                        hits++;
                    }
                }
                break;
        }
        return hits;
    }
}
