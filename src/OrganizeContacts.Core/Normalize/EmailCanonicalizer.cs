using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Normalize;

/// <summary>
/// Provider-aware email canonicalizer. Each rule is opt-in; the user can see, tune,
/// or disable each one. Returns the same string when no rule applies.
///
/// Rules covered:
/// - lowercase + trim everywhere
/// - googlemail.com -> gmail.com (Gmail)
/// - dots-in-local-part stripped (Gmail only)
/// - +tag stripped (Gmail, FastMail, ProtonMail, iCloud — configurable)
/// </summary>
public sealed class EmailCanonicalizer
{
    public bool LowercaseLocalPart { get; init; } = true;
    public bool MergeGoogleMailDomain { get; init; } = true;
    public bool StripGmailDots { get; init; } = true;
    public bool StripPlusTag { get; init; } = true;

    /// <summary>Domains where +tag stripping is safe per provider docs.</summary>
    public HashSet<string> PlusTagDomains { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com",
        "googlemail.com",
        "fastmail.com",
        "fastmail.fm",
        "protonmail.com",
        "proton.me",
        "icloud.com",
        "me.com",
        "mac.com",
        "outlook.com",
        "hotmail.com",
        "live.com",
    };

    public static EmailCanonicalizer Default { get; } = new();

    public string Canonicalize(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return string.Empty;

        var trimmed = address.Trim();
        var at = trimmed.LastIndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1) return trimmed.ToLowerInvariant();

        var local = trimmed[..at];
        var domain = trimmed[(at + 1)..].ToLowerInvariant();

        if (LowercaseLocalPart) local = local.ToLowerInvariant();

        var isGmail =
            domain.Equals("gmail.com", StringComparison.OrdinalIgnoreCase) ||
            domain.Equals("googlemail.com", StringComparison.OrdinalIgnoreCase);

        if (MergeGoogleMailDomain && domain.Equals("googlemail.com", StringComparison.OrdinalIgnoreCase))
            domain = "gmail.com";

        if (StripPlusTag && PlusTagDomains.Contains(domain))
        {
            var plus = local.IndexOf('+');
            if (plus >= 0) local = local[..plus];
        }

        if (StripGmailDots && isGmail)
            local = local.Replace(".", string.Empty);

        return $"{local}@{domain}";
    }

    public void Apply(Contact c)
    {
        for (int i = 0; i < c.Emails.Count; i++)
        {
            var e = c.Emails[i];
            if (!string.IsNullOrEmpty(e.CanonicalOverride)) continue;
            var canonical = Canonicalize(e.Address);
            if (canonical == e.Address.Trim().ToLowerInvariant()) continue; // no-op
            c.Emails[i] = new EmailAddress
            {
                Address = e.Address,
                CanonicalOverride = canonical,
                Kind = e.Kind,
                IsPreferred = e.IsPreferred,
                SourceId = e.SourceId,
            };
        }
    }
}
