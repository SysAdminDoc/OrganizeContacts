using OrganizeContacts.Core.Dedup;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public class DedupTests
{
    private static Contact Make(string fn,
        string? email = null,
        string? phone = null,
        string? phoneE164 = null,
        string? org = null)
    {
        var c = new Contact { FormattedName = fn, Organization = org };
        if (email is not null) c.Emails.Add(new EmailAddress { Address = email });
        if (phone is not null)
        {
            var p = PhoneNumber.Parse(phone);
            if (phoneE164 is not null)
                c.Phones.Add(new PhoneNumber { Raw = p.Raw, Digits = p.Digits, E164 = phoneE164, Kind = p.Kind });
            else
                c.Phones.Add(p);
        }
        return c;
    }

    [Fact]
    public void Exact_name_pair_is_grouped()
    {
        var engine = new DedupEngine();
        var groups = engine.Find(new[]
        {
            Make("John Doe", email: "john@example.com"),
            Make("john doe", email: "j@example.com"),
        });
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count);
        Assert.Contains(groups[0].Signals, s => s.Label.Contains("name"));
    }

    [Fact]
    public void E164_phone_match_groups_even_with_different_names()
    {
        var engine = new DedupEngine();
        var groups = engine.Find(new[]
        {
            Make("John Doe",   phone: "5551234567", phoneE164: "+15551234567"),
            Make("Jonathan D", phone: "5551234567", phoneE164: "+15551234567"),
        });
        Assert.Single(groups);
        Assert.Contains(groups[0].Signals, s => s.Label == "phone E.164");
    }

    [Fact]
    public void Email_canonical_match_groups_gmail_dot_variants()
    {
        var engine = new DedupEngine();
        var groups = engine.Find(new[]
        {
            Make("John Doe", email: "john.doe@gmail.com"),
            Make("J. Doe",   email: "johndoe@googlemail.com"),
        });
        Assert.Single(groups);
        Assert.Contains(groups[0].Signals, s => s.Label == "email canonical");
    }

    [Fact]
    public void Organization_alone_is_not_a_match()
    {
        var engine = new DedupEngine();
        var groups = engine.Find(new[]
        {
            Make("Alice", org: "Acme"),
            Make("Bob",   org: "Acme"),
        });
        Assert.Empty(groups);
    }

    [Fact]
    public void Strict_profile_demands_higher_evidence()
    {
        var loose = new DedupEngine(MatchRules.Loose);
        var strict = new DedupEngine(MatchRules.Strict);
        var input = new[]
        {
            Make("John Doe"),
            Make("Jon Doh"),
        };
        Assert.NotEmpty(loose.Find(input));
        Assert.Empty(strict.Find(input));
    }

    [Fact]
    public void Pair_score_returns_signal_breakdown()
    {
        var engine = new DedupEngine();
        var (conf, signals) = engine.ScorePair(
            Make("John Doe", email: "john@example.com", phone: "5551234567", phoneE164: "+15551234567"),
            Make("John Doe", email: "john@example.com", phone: "5551234567", phoneE164: "+15551234567"));
        Assert.True(conf > 0.9);
        Assert.Contains(signals, s => s.Label == "exact name");
        Assert.Contains(signals, s => s.Label == "phone E.164");
        Assert.Contains(signals, s => s.Label == "email canonical");
    }
}
