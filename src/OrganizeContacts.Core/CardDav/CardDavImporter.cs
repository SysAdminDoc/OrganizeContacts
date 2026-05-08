using System.IO;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.CardDav;

/// <summary>
/// Wraps CardDavClient + VCardImporter so a CardDAV address book can be consumed
/// through the same IContactImporter contract as a local file.
/// </summary>
public sealed class CardDavImporter : IContactImporter
{
    private readonly Func<CardDavClient> _clientFactory;
    private readonly string _addressBookUrl;

    public CardDavImporter(Func<CardDavClient> clientFactory, string addressBookUrl)
    {
        _clientFactory = clientFactory;
        _addressBookUrl = addressBookUrl;
    }

    public string Name => "CardDAV";
    public IReadOnlyCollection<string> SupportedExtensions { get; } = Array.Empty<string>();
    public bool CanRead(string path) => path == _addressBookUrl;

    public async IAsyncEnumerable<Contact> ReadAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = _clientFactory();
        var cards = await client.FetchAddressBookAsync(path, ct);
        var inner = new VCardImporter();

        foreach (var card in cards)
        {
            ct.ThrowIfCancellationRequested();
            // Stream the body through the existing parser by writing to a temp file.
            var tmp = Path.Combine(Path.GetTempPath(), $"oc-carddav-{Guid.NewGuid():N}.vcf");
            await File.WriteAllTextAsync(tmp, card.VCardBody, ct);
            try
            {
                await foreach (var c in inner.ReadAsync(tmp, ct))
                {
                    c.SourceFile = card.Href;
                    c.SourceFormat = $"CardDAV (etag {card.ETag})";
                    yield return c;
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }
    }
}
