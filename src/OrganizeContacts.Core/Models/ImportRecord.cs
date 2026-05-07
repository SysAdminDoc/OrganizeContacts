namespace OrganizeContacts.Core.Models;

public enum ImportStatus
{
    Pending,
    PreviewOnly,
    Committed,
    RolledBack,
    Failed,
}

public sealed class ImportRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SourceId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public ImportStatus Status { get; set; } = ImportStatus.Pending;
    public int ContactsCreated { get; set; }
    public int ContactsUpdated { get; set; }
    public int ContactsSkipped { get; set; }
    public string? Notes { get; set; }
}
