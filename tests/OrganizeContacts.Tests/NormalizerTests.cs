using OrganizeContacts.Core.Normalize;

namespace OrganizeContacts.Tests;

public class NormalizerTests
{
    [Theory]
    [InlineData("Dr. John Doe Jr.", "john doe")]
    [InlineData("MR JOHN DOE",       "john doe")]
    [InlineData("Anné  Müller",       "anne muller")]
    [InlineData("",                   "")]
    [InlineData("O'Brien-Smith",      "o'brien-smith")]
    public void Normalizes_names(string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.Normalize(input));
    }

    [Fact]
    public void Metaphone_groups_phonetically_similar_names()
    {
        Assert.Equal(NameNormalizer.Metaphone("Smith"), NameNormalizer.Metaphone("Smyth"));
        Assert.Equal(NameNormalizer.Metaphone("Catherine"), NameNormalizer.Metaphone("Katherine"));
    }

    [Theory]
    [InlineData("USER@gmail.com",      "user@gmail.com")]
    [InlineData("u.ser@gmail.com",     "user@gmail.com")]
    [InlineData("user+tag@gmail.com",  "user@gmail.com")]
    [InlineData("user@googlemail.com", "user@gmail.com")]
    [InlineData("u.s.e.r+tag@googlemail.com", "user@gmail.com")]
    [InlineData("USER@FastMail.com",   "user@fastmail.com")]
    [InlineData("user+tag@proton.me",  "user@proton.me")]
    [InlineData("user@example.org",    "user@example.org")]
    public void Canonicalizes_emails(string input, string expected)
    {
        var canon = new EmailCanonicalizer();
        Assert.Equal(expected, canon.Canonicalize(input));
    }

    [Fact]
    public void Email_canonicalization_can_be_tuned_off()
    {
        var canon = new EmailCanonicalizer
        {
            StripGmailDots = false,
            StripPlusTag = false,
            MergeGoogleMailDomain = false,
        };
        Assert.Equal("u.s.e.r+tag@googlemail.com", canon.Canonicalize("U.S.E.R+tag@GoogleMail.com"));
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("",       "",        0)]
    [InlineData("abc",    "",        3)]
    [InlineData("flaw",   "lawn",    2)]
    public void Levenshtein_distance(string a, string b, int expected)
    {
        Assert.Equal(expected, Levenshtein.Distance(a, b));
    }

    [Fact]
    public void Phone_normalizer_produces_E164_for_us_numbers()
    {
        var n = new PhoneNormalizer("US");
        Assert.Equal("+15551234567", n.ToE164("(555) 123-4567"));
        Assert.Equal("+15551234567", n.ToE164("555-123-4567"));
    }

    [Fact]
    public void Phone_normalizer_handles_international()
    {
        var n = new PhoneNormalizer("US");
        Assert.Equal("+442071234567", n.ToE164("+44 20 7123 4567"));
    }
}
