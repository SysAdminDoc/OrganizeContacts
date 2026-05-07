namespace OrganizeContacts.Core.Models;

public enum EmailKind { Other, Personal, Work }

public sealed class EmailAddress
{
    public string Address { get; init; } = string.Empty;
    public EmailKind Kind { get; init; } = EmailKind.Other;
    public bool IsPreferred { get; init; }

    public string Canonical => Address.Trim().ToLowerInvariant();
}
