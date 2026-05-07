namespace OrganizeContacts.Core.Models;

public enum EmailKind { Other, Personal, Work }

public sealed class EmailAddress
{
    public string Address { get; init; } = string.Empty;
    public string? CanonicalOverride { get; init; }
    public EmailKind Kind { get; init; } = EmailKind.Other;
    public bool IsPreferred { get; init; }
    public Guid? SourceId { get; init; }

    public string Canonical =>
        !string.IsNullOrWhiteSpace(CanonicalOverride) ? CanonicalOverride!
        : Address.Trim().ToLowerInvariant();
}
