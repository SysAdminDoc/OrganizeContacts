using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using OrganizeContacts.Core.CardDav;

namespace OrganizeContacts.Tests;

public class CardDavTests
{
    [Fact]
    public void Parses_address_book_listing_into_href_etag_pairs()
    {
        var xml = XElement.Parse("""
            <multistatus xmlns="DAV:">
              <response>
                <href>/addressbooks/matt/contacts/</href>
                <propstat><prop><getetag>"abc123"</getetag></prop></propstat>
              </response>
              <response>
                <href>/addressbooks/matt/contacts/jane.vcf</href>
                <propstat><prop><getetag>"v1"</getetag></prop></propstat>
              </response>
              <response>
                <href>/addressbooks/matt/contacts/john.vcf</href>
                <propstat><prop><getetag>"v2"</getetag></prop></propstat>
              </response>
            </multistatus>
            """);

        var entries = CardDavClient.ParseAddressBookListing(xml);
        // The collection root (trailing /) is filtered out.
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Href.EndsWith("jane.vcf") && e.ETag == "v1");
        Assert.Contains(entries, e => e.Href.EndsWith("john.vcf") && e.ETag == "v2");
    }

    [Fact]
    public async Task Discovers_principal_via_propfind()
    {
        var handler = new ScriptedHandler(req =>
        {
            if (req.Method.Method == "PROPFIND" && req.RequestUri!.AbsolutePath.Contains("well-known"))
            {
                var body = """
                    <multistatus xmlns="DAV:">
                      <response>
                        <propstat><prop><current-user-principal>
                          <href>/principals/matt/</href>
                        </current-user-principal></prop></propstat>
                      </response>
                    </multistatus>
                    """;
                return new HttpResponseMessage(HttpStatusCode.MultiStatus)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/xml"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://dav.example.com/") };
        var client = new CardDavClient(http);
        var principal = await client.FindCurrentUserPrincipalAsync(CancellationToken.None);
        Assert.Equal("/principals/matt/", principal);
    }

    [Fact]
    public async Task Lists_address_books_with_displaynames()
    {
        var handler = new ScriptedHandler(req =>
        {
            var body = """
                <multistatus xmlns="DAV:" xmlns:c="urn:ietf:params:xml:ns:carddav">
                  <response>
                    <href>/addressbooks/matt/</href>
                    <propstat><prop>
                      <displayname>Matt's books</displayname>
                      <resourcetype><collection/></resourcetype>
                    </prop></propstat>
                  </response>
                  <response>
                    <href>/addressbooks/matt/contacts/</href>
                    <propstat><prop>
                      <displayname>Default</displayname>
                      <resourcetype><collection/><c:addressbook/></resourcetype>
                    </prop></propstat>
                  </response>
                  <response>
                    <href>/addressbooks/matt/work/</href>
                    <propstat><prop>
                      <displayname>Work</displayname>
                      <resourcetype><collection/><c:addressbook/></resourcetype>
                    </prop></propstat>
                  </response>
                </multistatus>
                """;
            return new HttpResponseMessage(HttpStatusCode.MultiStatus)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml"),
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://dav.example.com/") };
        var client = new CardDavClient(http);
        var books = await client.ListAddressBooksAsync("/addressbooks/matt/", CancellationToken.None);
        Assert.Equal(2, books.Count);
        Assert.Contains(books, b => b.DisplayName == "Default");
        Assert.Contains(books, b => b.DisplayName == "Work");
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> h) => _handler = h;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_handler(request));
    }
}
