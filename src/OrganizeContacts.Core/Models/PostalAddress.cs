namespace OrganizeContacts.Core.Models;

public enum AddressKind { Other, Home, Work }

public sealed class PostalAddress
{
    public string? PoBox { get; init; }
    public string? Extended { get; init; }
    public string? Street { get; init; }
    public string? Locality { get; init; }
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public AddressKind Kind { get; init; } = AddressKind.Other;
    public Guid? SourceId { get; init; }

    public string OneLine => string.Join(", ",
        new[] { Street, Locality, Region, PostalCode, Country }
            .Where(p => !string.IsNullOrWhiteSpace(p))!);
}
