using System.Globalization;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Normalize;

namespace OrganizeContacts.Core.Dedup;

/// <summary>
/// Two-stage matcher:
/// 1. Blocking — group contacts by cheap keys (normalized name, phone E.164/last7, email canonical, name metaphone)
///    so the O(n^2) work only happens within a block.
/// 2. Pair scoring — within each block, score every pair using configurable weights and emit a
///    DuplicateGroup with Signals describing the contributing evidence.
/// </summary>
public sealed class DedupEngine
{
    private readonly MatchRules _rules;
    private readonly EmailCanonicalizer _emailCanon;

    public DedupEngine(MatchRules? rules = null, EmailCanonicalizer? emailCanon = null)
    {
        _rules = rules ?? MatchRules.Default;
        _emailCanon = emailCanon ?? EmailCanonicalizer.Default;
    }

    public IReadOnlyList<DuplicateGroup> Find(IEnumerable<Contact> contacts)
    {
        var list = contacts.ToList();
        if (list.Count < 2) return Array.Empty<DuplicateGroup>();

        // Stage 1 — blocking
        var blocks = new Dictionary<string, List<Contact>>(StringComparer.Ordinal);
        foreach (var c in list)
            foreach (var key in BlockKeys(c))
            {
                if (!blocks.TryGetValue(key, out var bucket))
                    blocks[key] = bucket = new List<Contact>();
                if (!bucket.Contains(c)) bucket.Add(c);
            }

        // Stage 2 — pair scoring within blocks (union-find merges related pairs into a group)
        var parent = new Dictionary<Guid, Guid>();
        var pairScores = new Dictionary<(Guid, Guid), (double conf, List<MatchSignal> signals)>();

        foreach (var c in list) parent[c.Id] = c.Id;

        Guid Find(Guid id)
        {
            while (parent[id] != id)
            {
                parent[id] = parent[parent[id]];
                id = parent[id];
            }
            return id;
        }

        void Union(Guid a, Guid b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        foreach (var bucket in blocks.Values)
        {
            if (bucket.Count < 2) continue;
            for (int i = 0; i < bucket.Count; i++)
            {
                for (int j = i + 1; j < bucket.Count; j++)
                {
                    var (conf, signals) = ScorePair(bucket[i], bucket[j]);
                    if (conf < _rules.ReviewThreshold) continue;

                    var key = bucket[i].Id.CompareTo(bucket[j].Id) < 0
                        ? (bucket[i].Id, bucket[j].Id)
                        : (bucket[j].Id, bucket[i].Id);
                    if (!pairScores.TryGetValue(key, out var existing) || existing.conf < conf)
                        pairScores[key] = (conf, signals);
                    Union(bucket[i].Id, bucket[j].Id);
                }
            }
        }

        if (pairScores.Count == 0) return Array.Empty<DuplicateGroup>();

        // Stage 3 — assemble groups by union-find root
        var byRoot = new Dictionary<Guid, DuplicateGroup>();
        var byId = list.ToDictionary(c => c.Id);
        foreach (var ((a, b), (conf, signals)) in pairScores)
        {
            var root = Find(a);
            if (!byRoot.TryGetValue(root, out var group))
            {
                group = new DuplicateGroup();
                byRoot[root] = group;
            }
            if (!group.Members.Contains(byId[a])) group.Members.Add(byId[a]);
            if (!group.Members.Contains(byId[b])) group.Members.Add(byId[b]);

            foreach (var s in signals)
            {
                if (!group.Signals.Any(x => x.Label == s.Label))
                    group.Signals.Add(s);
            }
            if (conf > group.Confidence) group.Confidence = conf;
        }

        foreach (var g in byRoot.Values)
        {
            g.MatchReason = string.Join(" + ", g.Signals.OrderByDescending(s => s.Weight).Select(s => s.Label));
            if (string.IsNullOrEmpty(g.MatchReason)) g.MatchReason = "matched";
        }
        return byRoot.Values.ToList();
    }

    /// <summary>Generates blocking keys for a single contact.</summary>
    private IEnumerable<string> BlockKeys(Contact c)
    {
        if (_rules.MatchOnNormalizedName)
        {
            var name = NameNormalizer.Normalize(c.DisplayName);
            if (!string.IsNullOrEmpty(name)) yield return $"name|{name}";

            var meta = NameNormalizer.Metaphone(c.DisplayName);
            if (!string.IsNullOrEmpty(meta)) yield return $"meta|{meta}";
        }

        if (_rules.MatchOnPhoneE164 || _rules.MatchOnPhoneLast7)
            foreach (var p in c.Phones)
            {
                if (_rules.MatchOnPhoneE164 && !string.IsNullOrEmpty(p.E164))
                    yield return $"e164|{p.E164}";
                if (_rules.MatchOnPhoneLast7 && p.Digits.Length >= _rules.MinPhoneDigits)
                    yield return $"tel|{p.Digits[^_rules.MinPhoneDigits..]}";
            }

        if (_rules.MatchOnEmailCanonical)
            foreach (var e in c.Emails)
                if (!string.IsNullOrWhiteSpace(e.Address))
                    yield return $"email|{_emailCanon.Canonicalize(e.Address)}";
    }

    /// <summary>Scores a pair of candidates with explainable per-field signals.</summary>
    public (double Confidence, List<MatchSignal> Signals) ScorePair(Contact a, Contact b)
    {
        var signals = new List<MatchSignal>();
        double total = 0;

        var nameA = NameNormalizer.Normalize(a.DisplayName);
        var nameB = NameNormalizer.Normalize(b.DisplayName);
        if (!string.IsNullOrEmpty(nameA) && !string.IsNullOrEmpty(nameB))
        {
            if (nameA == nameB)
            {
                signals.Add(new MatchSignal("exact name", _rules.WeightExactName, $"both '{a.DisplayName}'"));
                total += _rules.WeightExactName;
            }
            else
            {
                var sim = Levenshtein.Similarity(nameA, nameB);
                if (sim >= _rules.NameSimilarityFloor)
                {
                    var weight = _rules.WeightFuzzyName * sim;
                    signals.Add(new MatchSignal("similar name", weight, $"'{a.DisplayName}' ~ '{b.DisplayName}' ({sim:P0})"));
                    total += weight;
                }
                var ma = NameNormalizer.Metaphone(a.DisplayName);
                var mb = NameNormalizer.Metaphone(b.DisplayName);
                if (!string.IsNullOrEmpty(ma) && ma == mb)
                {
                    signals.Add(new MatchSignal("phonetic name", _rules.WeightMetaphone, $"metaphone={ma}"));
                    total += _rules.WeightMetaphone;
                }
            }
        }

        // Phones — keep blocking-key digit count and pair-scoring digit count in sync
        // so a contact pair grouped on a tail-N match also scores on it.
        var tail = Math.Max(_rules.MinPhoneDigits, 1);
        foreach (var pa in a.Phones)
        foreach (var pb in b.Phones)
        {
            if (!string.IsNullOrEmpty(pa.E164) && pa.E164 == pb.E164)
            {
                signals.Add(new MatchSignal("phone E.164", _rules.WeightPhoneE164, pa.E164!));
                total += _rules.WeightPhoneE164;
                goto phonesDone;
            }
            if (pa.Digits.Length >= tail && pb.Digits.Length >= tail &&
                pa.Digits[^tail..] == pb.Digits[^tail..])
            {
                var label = $"phone last {tail}";
                signals.Add(new MatchSignal(label, _rules.WeightPhoneLast7, pa.Digits[^tail..]));
                total += _rules.WeightPhoneLast7;
                goto phonesDone;
            }
        }
        phonesDone:

        // Emails
        foreach (var ea in a.Emails)
        foreach (var eb in b.Emails)
        {
            var ca = _emailCanon.Canonicalize(ea.Address);
            var cb = _emailCanon.Canonicalize(eb.Address);
            if (!string.IsNullOrWhiteSpace(ca) && ca == cb)
            {
                signals.Add(new MatchSignal("email canonical", _rules.WeightEmailCanonical, ca));
                total += _rules.WeightEmailCanonical;
                goto emailsDone;
            }
        }
        emailsDone:

        // Organization (mild signal)
        if (!string.IsNullOrWhiteSpace(a.Organization) && !string.IsNullOrWhiteSpace(b.Organization) &&
            a.Organization.Equals(b.Organization, StringComparison.OrdinalIgnoreCase))
        {
            signals.Add(new MatchSignal("organization", _rules.WeightOrganization, a.Organization));
            total += _rules.WeightOrganization;
        }

        // False-positive guardrail: pure organization match without name/phone/email is not a person dupe.
        var hasIdentitySignal = signals.Any(s =>
            s.Label.StartsWith("phone", StringComparison.OrdinalIgnoreCase) ||
            s.Label.StartsWith("email", StringComparison.OrdinalIgnoreCase) ||
            s.Label.Contains("name", StringComparison.OrdinalIgnoreCase));
        if (!hasIdentitySignal) total = 0;

        return (Math.Min(total, 1.0), signals);
    }

    public bool IsAutoMerge(double confidence) => confidence >= _rules.AutoMergeThreshold;

    public static string NormalizeName(string? input) => NameNormalizer.Normalize(input);
}
