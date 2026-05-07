namespace OrganizeContacts.Core.Dedup;

public sealed class MatchRules
{
    public bool MatchOnNormalizedName { get; init; } = true;
    public bool MatchOnPhoneLast7 { get; init; } = true;
    public bool MatchOnEmailCanonical { get; init; } = true;
    public int MinPhoneDigits { get; init; } = 7;

    public static MatchRules Default { get; } = new();
}
