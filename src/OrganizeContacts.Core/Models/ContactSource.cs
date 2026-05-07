namespace OrganizeContacts.Core.Models;

public enum SourceKind
{
    Unknown,
    File,
    GoogleCsv,
    OutlookCsv,
    OutlookPst,
    CardDav,
    AndroidVcf,
    Thunderbird,
    Manual,
}

public sealed class ContactSource
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public SourceKind Kind { get; init; } = SourceKind.Unknown;
    public string Label { get; init; } = string.Empty;
    public string? FilePath { get; init; }
    public string? Account { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string Display => string.IsNullOrWhiteSpace(Account) ? Label : $"{Label} · {Account}";
}
