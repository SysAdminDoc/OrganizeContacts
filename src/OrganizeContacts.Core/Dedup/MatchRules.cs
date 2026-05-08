namespace OrganizeContacts.Core.Dedup;

/// <summary>
/// Configurable matching weights and thresholds. The default profile is "Balanced".
/// Each weight contributes to the total confidence score (0..1) when the field matches.
/// </summary>
public sealed class MatchRules
{
    // Hard signals (match key generators, used for blocking and exact-match detection)
    public bool MatchOnNormalizedName { get; init; } = true;
    public bool MatchOnPhoneE164 { get; init; } = true;
    public bool MatchOnPhoneLast7 { get; init; } = true;
    public bool MatchOnEmailCanonical { get; init; } = true;
    public int MinPhoneDigits { get; init; } = 7;

    // Weighted scorer
    public double WeightExactName { get; init; } = 0.50;
    public double WeightFuzzyName { get; init; } = 0.35;
    public double WeightPhoneE164 { get; init; } = 0.45;
    public double WeightPhoneLast7 { get; init; } = 0.20;
    public double WeightEmailCanonical { get; init; } = 0.45;
    public double WeightOrganization { get; init; } = 0.10;
    public double WeightMetaphone { get; init; } = 0.20;

    // Acceptance
    public double AutoMergeThreshold { get; init; } = 0.95;
    public double ReviewThreshold { get; init; } = 0.55;
    public double NameSimilarityFloor { get; init; } = 0.78;

    public static MatchRules Default { get; } = new();

    public static MatchRules Strict { get; } = new()
    {
        WeightFuzzyName = 0.20,
        AutoMergeThreshold = 0.98,
        ReviewThreshold = 0.75,
        NameSimilarityFloor = 0.90,
    };

    public static MatchRules Loose { get; } = new()
    {
        WeightFuzzyName = 0.45,
        AutoMergeThreshold = 0.90,
        ReviewThreshold = 0.40,
        NameSimilarityFloor = 0.65,
    };
}
