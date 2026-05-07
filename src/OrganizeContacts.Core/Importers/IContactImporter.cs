using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

public interface IContactImporter
{
    string Name { get; }
    IReadOnlyCollection<string> SupportedExtensions { get; }
    bool CanRead(string path);
    IAsyncEnumerable<Contact> ReadAsync(string path, CancellationToken ct = default);
}
