using OrganizeContacts.Core.Dedup;
using OrganizeContacts.Core.Merge;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public class AutoMergeTests
{
    private static Contact Make(string fn, string? phone = null, string? email = null, string? org = null, string? title = null)
    {
        var c = new Contact { FormattedName = fn, Organization = org, Title = title };
        if (phone is not null)
            c.Phones.Add(new PhoneNumber { Raw = phone, Digits = new string(phone.Where(char.IsDigit).ToArray()), E164 = phone.StartsWith("+") ? phone : null });
        if (email is not null) c.Emails.Add(new EmailAddress { Address = email });
        return c;
    }

    [Fact]
    public void Plans_subset_secondary()
    {
        // Primary is the rich one; secondary has fewer fields.
        var primary = Make("John Doe", "+15551234567", "john@example.com", "Acme", "CTO");
        var secondary = Make("John Doe", "+15551234567");

        var group = new DuplicateGroup { Confidence = 1.0 };
        group.Members.Add(primary);
        group.Members.Add(secondary);

        var report = new AutoMergeService().Plan(new[] { group }, MatchRules.Default.AutoMergeThreshold);
        Assert.Single(report.Plans);
    }

    [Fact]
    public void Skips_when_neither_is_a_subset()
    {
        // Each card carries a piece of unique information the other lacks.
        var a = Make("John Doe", "+15551234567", "alice@example.com", "Org A");
        var b = Make("John Doe", "+15551234567", "bob@example.com",   "Org B");
        var group = new DuplicateGroup { Confidence = 1.0 };
        group.Members.Add(a);
        group.Members.Add(b);

        var report = new AutoMergeService().Plan(new[] { group }, MatchRules.Default.AutoMergeThreshold);
        Assert.Empty(report.Plans);
        Assert.Equal(1, report.Skipped);
    }

    [Fact]
    public void Skips_below_confidence_threshold()
    {
        var primary = Make("John Doe", "+15551234567");
        var secondary = Make("John Doe", "+15551234567");
        var group = new DuplicateGroup { Confidence = 0.50 };
        group.Members.Add(primary);
        group.Members.Add(secondary);

        var report = new AutoMergeService().Plan(new[] { group }, threshold: 0.95);
        Assert.Empty(report.Plans);
    }
}
