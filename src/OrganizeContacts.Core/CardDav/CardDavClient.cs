using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace OrganizeContacts.Core.CardDav;

public sealed record CardDavCard(string Href, string ETag, string VCardBody);
public sealed record AddressBookInfo(string Url, string DisplayName);

/// <summary>
/// Minimal read-only CardDAV client — discover, enumerate, fetch.
/// HttpClient is injectable so tests can mock with a fake handler.
/// Uses Basic auth (compatible with Nextcloud, Baikal, Radicale, iCloud
/// app-specific passwords, Fastmail app-passwords). OAuth flows are out of scope.
/// </summary>
public sealed class CardDavClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <summary>Used by tests / DI: caller owns the HttpClient lifetime.</summary>
    public CardDavClient(HttpClient http)
    {
        _http = http;
        _ownsHttp = false;
    }

    public CardDavClient(Uri baseUri, string username, string password)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true,
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            // Cap server hangs — without this, a misconfigured server can freeze the dialog.
            Timeout = TimeSpan.FromSeconds(60),
        };
        _ownsHttp = true;
        var bytes = Encoding.UTF8.GetBytes($"{username}:{password}");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OrganizeContacts/0.3 (+local-first)");
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    /// <summary>
    /// Walks the discovery chain: well-known URL -> current-user-principal
    /// -> addressbook-home-set -> address books.
    /// </summary>
    public async Task<IReadOnlyList<AddressBookInfo>> DiscoverAddressBooksAsync(CancellationToken ct = default)
    {
        var principal = await FindCurrentUserPrincipalAsync(ct);
        if (principal is null) return Array.Empty<AddressBookInfo>();

        var home = await FindAddressBookHomeSetAsync(principal, ct);
        if (home is null) return Array.Empty<AddressBookInfo>();

        return await ListAddressBooksAsync(home, ct);
    }

    public async Task<IReadOnlyList<CardDavCard>> FetchAddressBookAsync(string addressBookUrl, CancellationToken ct = default)
    {
        // 1) Enumerate hrefs + ETags
        var index = await PropfindAsync(addressBookUrl, depth: "1", body: PropfindBodyForCards, ct);
        var entries = ParseAddressBookListing(index);

        // 2) Fetch each card body
        var cards = new List<CardDavCard>(entries.Count);
        foreach (var (href, etag) in entries)
        {
            ct.ThrowIfCancellationRequested();
            var resolved = ResolveHref(addressBookUrl, href);
            using var req = new HttpRequestMessage(HttpMethod.Get, resolved);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) continue;
            var body = await resp.Content.ReadAsStringAsync(ct);
            cards.Add(new CardDavCard(href, etag, body));
        }
        return cards;
    }

    // ----- discovery helpers -----

    internal async Task<string?> FindCurrentUserPrincipalAsync(CancellationToken ct)
    {
        foreach (var path in new[] { ".well-known/carddav", "" })
        {
            var xml = await PropfindAsync(path, depth: "0", body: PropfindBodyForPrincipal, ct);
            var pr = xml?.Descendants("{DAV:}current-user-principal")
                       .Elements("{DAV:}href").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(pr)) return pr;
        }
        return null;
    }

    internal async Task<string?> FindAddressBookHomeSetAsync(string principalUrl, CancellationToken ct)
    {
        var xml = await PropfindAsync(principalUrl, depth: "0", body: PropfindBodyForHomeSet, ct);
        var home = xml?.Descendants("{urn:ietf:params:xml:ns:carddav}addressbook-home-set")
                      .Elements("{DAV:}href").FirstOrDefault()?.Value;
        return string.IsNullOrWhiteSpace(home) ? null : home;
    }

    internal async Task<IReadOnlyList<AddressBookInfo>> ListAddressBooksAsync(string homeUrl, CancellationToken ct)
    {
        var xml = await PropfindAsync(homeUrl, depth: "1", body: PropfindBodyForAddressBookList, ct);
        if (xml is null) return Array.Empty<AddressBookInfo>();

        var list = new List<AddressBookInfo>();
        foreach (var resp in xml.Descendants("{DAV:}response"))
        {
            var href = resp.Elements("{DAV:}href").FirstOrDefault()?.Value;
            var props = resp.Descendants("{DAV:}prop").FirstOrDefault();
            var resourceType = props?.Element("{DAV:}resourcetype");
            var isAddressBook = resourceType?.Element("{urn:ietf:params:xml:ns:carddav}addressbook") is not null;
            var displayName = props?.Element("{DAV:}displayname")?.Value;
            if (isAddressBook && !string.IsNullOrEmpty(href))
                list.Add(new AddressBookInfo(href!, displayName ?? href!));
        }
        return list;
    }

    // ----- low-level wire -----

    internal async Task<XElement?> PropfindAsync(string path, string depth, string body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml"),
        };
        req.Headers.Add("Depth", depth);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return XElement.Parse(raw); }
        catch { return null; }
    }

    internal static IReadOnlyList<(string Href, string ETag)> ParseAddressBookListing(XElement? root)
    {
        if (root is null) return Array.Empty<(string, string)>();
        var list = new List<(string, string)>();
        foreach (var resp in root.Descendants("{DAV:}response"))
        {
            var href = resp.Element("{DAV:}href")?.Value;
            if (string.IsNullOrEmpty(href)) continue;
            // Skip the address book root itself (no .vcf body)
            if (href.EndsWith("/")) continue;
            var etag = resp.Descendants("{DAV:}getetag").FirstOrDefault()?.Value ?? "";
            list.Add((href!, etag.Trim('"')));
        }
        return list;
    }

    internal static string ResolveHref(string baseAddressBookUrl, string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out _)) return href;
        return href; // server returned an absolute path; HttpClient.BaseAddress handles it
    }

    private const string PropfindBodyForPrincipal = """
        <?xml version="1.0" encoding="utf-8"?>
        <propfind xmlns="DAV:">
          <prop><current-user-principal/></prop>
        </propfind>
        """;

    private const string PropfindBodyForHomeSet = """
        <?xml version="1.0" encoding="utf-8"?>
        <propfind xmlns="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav">
          <prop><c:addressbook-home-set/></prop>
        </propfind>
        """;

    private const string PropfindBodyForAddressBookList = """
        <?xml version="1.0" encoding="utf-8"?>
        <propfind xmlns="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav">
          <prop>
            <displayname/>
            <resourcetype/>
          </prop>
        </propfind>
        """;

    private const string PropfindBodyForCards = """
        <?xml version="1.0" encoding="utf-8"?>
        <propfind xmlns="DAV:">
          <prop>
            <getetag/>
            <getcontenttype/>
          </prop>
        </propfind>
        """;
}
