namespace OrganizeContacts.Core.Models;

public sealed class DuplicateGroup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public List<Contact> Members { get; } = new();
    public string MatchReason { get; set; } = string.Empty;
    public double Confidence { get; set; }

    /// <summary>Per-pair score breakdown so the UI can show "matched on phone (+0.45), email (+0.45)".</summary>
    public List<MatchSignal> Signals { get; } = new();

    public Contact Primary => Members[0];
    public int Count => Members.Count;
}

public sealed record MatchSignal(string Label, double Weight, string Detail);
