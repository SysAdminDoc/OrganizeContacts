using PhoneNumbers;
using OrganizeContacts.Core.Models;
using ModelPhone = OrganizeContacts.Core.Models.PhoneNumber;

namespace OrganizeContacts.Core.Normalize;

/// <summary>
/// libphonenumber-csharp backed normalizer. Falls back to digits-only on parse failure
/// (e.g. shared numbers, intl prefixes the user keyed in oddly). Region defaults to US.
/// </summary>
public sealed class PhoneNormalizer
{
    private readonly PhoneNumberUtil _util;
    public string DefaultRegion { get; }

    public PhoneNormalizer(string defaultRegion = "US")
    {
        DefaultRegion = string.IsNullOrWhiteSpace(defaultRegion) ? "US" : defaultRegion.ToUpperInvariant();
        _util = PhoneNumberUtil.GetInstance();
    }

    /// <summary>Best-effort E.164 like "+15551234567"; returns null when unparseable.</summary>
    public string? ToE164(string raw, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var parsed = _util.Parse(raw, region ?? DefaultRegion);
            // Accept "possible" (IsPossibleNumber) numbers — fictional 555-1234567 etc.
            // are common in test data but valid for our normalization purposes.
            if (!_util.IsPossibleNumber(parsed)) return null;
            return _util.Format(parsed, PhoneNumberFormat.E164);
        }
        catch (NumberParseException)
        {
            return null;
        }
    }

    /// <summary>Stamps each phone with E164 in-place when libphonenumber accepts it.</summary>
    public void Normalize(Contact c, string? region = null)
    {
        for (int i = 0; i < c.Phones.Count; i++)
        {
            var p = c.Phones[i];
            if (!string.IsNullOrEmpty(p.E164)) continue;
            var e164 = ToE164(p.Raw, region);
            if (e164 is null) continue;
            c.Phones[i] = new ModelPhone
            {
                Raw = p.Raw,
                Digits = p.Digits,
                E164 = e164,
                Kind = p.Kind,
                IsPreferred = p.IsPreferred,
                SourceId = p.SourceId,
            };
        }
    }

    /// <summary>Best dedupe key: E.164 if available, else last 7 digits.</summary>
    public static string DedupeKey(ModelPhone p)
    {
        if (!string.IsNullOrEmpty(p.E164)) return $"e164|{p.E164}";
        if (p.Digits.Length >= 7) return $"tail7|{p.Digits[^7..]}";
        return $"raw|{p.Digits}";
    }
}
