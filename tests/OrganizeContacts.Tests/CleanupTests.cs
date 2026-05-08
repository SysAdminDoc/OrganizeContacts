using OrganizeContacts.Core.Cleanup;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Normalize;

namespace OrganizeContacts.Tests;

public class CleanupTests
{
    [Fact]
    public void Dedupes_phones_within_a_contact_by_e164()
    {
        var c = new Contact { FormattedName = "x" };
        c.Phones.Add(new PhoneNumber { Raw = "(555) 123-4567", Digits = "5551234567", E164 = "+15551234567" });
        c.Phones.Add(new PhoneNumber { Raw = "5551234567",     Digits = "5551234567", E164 = "+15551234567" });
        c.Phones.Add(new PhoneNumber { Raw = "555-9999",       Digits = "5559999",    E164 = "+15555559999" });

        var report = new BatchCleanup().Run(new[] { c });
        Assert.Equal(2, c.Phones.Count);
        Assert.Equal(1, report.PhonesDeduped);
        Assert.Equal(1, report.ContactsTouched);
    }

    [Fact]
    public void Dedupes_emails_with_provider_canonicalization()
    {
        var c = new Contact { FormattedName = "x" };
        c.Emails.Add(new EmailAddress { Address = "John.Doe@gmail.com" });
        c.Emails.Add(new EmailAddress { Address = "johndoe@googlemail.com" });
        c.Emails.Add(new EmailAddress { Address = "Other@example.com" });

        var report = new BatchCleanup().Run(new[] { c });
        Assert.Equal(2, c.Emails.Count);
        Assert.Equal(1, report.EmailsDeduped);
    }

    [Fact]
    public void Regex_edit_replaces_in_chosen_field()
    {
        var c = new Contact { FormattedName = "Mr John Doe" };
        var report = new BatchCleanup().Run(
            new[] { c },
            dedupePhones: false, dedupeEmails: false, dedupeUrls: false, dedupeCategories: false,
            normalizePhones: false, canonicalizeEmails: false,
            regexEdits: new[] { new RegexEdit(RegexTarget.FormattedName, @"^Mr\s+", "") });
        Assert.Equal("John Doe", c.FormattedName);
        Assert.Equal(1, report.RegexHits);
    }
}
