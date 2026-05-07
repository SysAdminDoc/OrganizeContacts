namespace OrganizeContacts.Core.Models;

public sealed class Contact
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? FormattedName { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? AdditionalNames { get; set; }
    public string? HonorificPrefix { get; set; }
    public string? HonorificSuffix { get; set; }
    public string? Nickname { get; set; }
    public string? Organization { get; set; }
    public string? Title { get; set; }
    public DateOnly? Birthday { get; set; }
    public string? Notes { get; set; }
    public byte[]? PhotoBytes { get; set; }
    public string? PhotoMimeType { get; set; }
    public string? SourceFile { get; set; }
    public string? SourceFormat { get; set; }
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;

    public List<PhoneNumber> Phones { get; } = new();
    public List<EmailAddress> Emails { get; } = new();
    public List<PostalAddress> Addresses { get; } = new();
    public List<string> Categories { get; } = new();
    public List<string> Urls { get; } = new();

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(FormattedName) ? FormattedName!
        : string.Join(' ', new[] { GivenName, FamilyName }.Where(s => !string.IsNullOrWhiteSpace(s)))
            .Trim();
}
