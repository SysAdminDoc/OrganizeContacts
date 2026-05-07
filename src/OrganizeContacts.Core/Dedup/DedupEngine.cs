using System.Globalization;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Dedup;

public sealed class DedupEngine
{
    private readonly MatchRules _rules;

    public DedupEngine(MatchRules? rules = null) => _rules = rules ?? MatchRules.Default;

    public IReadOnlyList<DuplicateGroup> Find(IEnumerable<Contact> contacts)
    {
        var contactList = contacts.ToList();
        var byKey = new Dictionary<string, List<Contact>>(StringComparer.Ordinal);

        foreach (var c in contactList)
        {
            foreach (var key in KeysFor(c))
            {
                if (!byKey.TryGetValue(key, out var bucket))
                    byKey[key] = bucket = new List<Contact>();
                if (!bucket.Contains(c)) bucket.Add(c);
            }
        }

        var seenIds = new HashSet<Guid>();
        var groups = new List<DuplicateGroup>();

        foreach (var (key, bucket) in byKey)
        {
            if (bucket.Count < 2) continue;
            if (bucket.All(b => seenIds.Contains(b.Id))) continue;

            var group = new DuplicateGroup
            {
                MatchReason = DescribeKey(key),
                Confidence = 1.0,
            };
            foreach (var c in bucket) group.Members.Add(c);
            foreach (var c in bucket) seenIds.Add(c.Id);
            groups.Add(group);
        }

        return groups;
    }

    private IEnumerable<string> KeysFor(Contact c)
    {
        if (_rules.MatchOnNormalizedName)
        {
            var name = NormalizeName(c.DisplayName);
            if (!string.IsNullOrEmpty(name)) yield return $"name|{name}";
        }

        if (_rules.MatchOnPhoneLast7)
        {
            foreach (var p in c.Phones)
            {
                if (p.Digits.Length >= _rules.MinPhoneDigits)
                    yield return $"tel|{p.Digits[^_rules.MinPhoneDigits..]}";
            }
        }

        if (_rules.MatchOnEmailCanonical)
        {
            foreach (var e in c.Emails)
                if (!string.IsNullOrWhiteSpace(e.Address))
                    yield return $"email|{e.Canonical}";
        }
    }

    private static string DescribeKey(string key)
    {
        var pipe = key.IndexOf('|');
        if (pipe < 0) return "exact match";
        return key[..pipe] switch
        {
            "name" => "matching name",
            "tel" => "matching phone (last 7 digits)",
            "email" => "matching email",
            _ => "exact match",
        };
    }

    public static string NormalizeName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var lower = input.Trim().ToLower(CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }
}
