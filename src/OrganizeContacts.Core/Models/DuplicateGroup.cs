namespace OrganizeContacts.Core.Models;

public sealed class DuplicateGroup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public List<Contact> Members { get; } = new();
    public string MatchReason { get; init; } = string.Empty;
    public double Confidence { get; init; }

    public Contact Primary => Members[0];
    public int Count => Members.Count;
}
