using System.Text;
using OrganizeContacts.Core.Importers;

namespace OrganizeContacts.Tests;

public sealed class ParserRobustnessTests
{
    [Fact]
    public void VCard_parser_survives_deterministic_malformed_input_fuzz_cases()
    {
        var random = new Random(0x0C0FFEE);
        var importer = new VCardImporter();
        const string alphabet = "BEGIN:VCARD\nEND:VCARD\r\nVERSION:4.0\nFN:\\n;,:=\\\\\t";

        for (var caseNumber = 0; caseNumber < 500; caseNumber++)
        {
            var length = random.Next(0, 320);
            var body = new StringBuilder(length + 80);
            body.Append("BEGIN:VCARD\nVERSION:");
            for (var i = 0; i < length; i++)
            {
                if (random.Next(5) == 0)
                    body.Append(alphabet[random.Next(alphabet.Length)]);
                else
                    body.Append((char)random.Next(0x20, 0x100));
            }
            body.Append("\nPHOTO;ENCODING=B:not-base64\nEND:VCARD\n");

            var exception = Record.Exception(() => importer.ParseAll(body.ToString()).ToList());
            Assert.Null(exception);
        }
    }

    [Fact]
    public void Demo_corpus_round_trips_through_vcard_writer()
    {
        var path = FindDemoVCard();
        var importer = new VCardImporter();
        var source = importer.ParseAll(File.ReadAllText(path), path).ToList();
        var output = new VCardWriter().WriteAll(source);
        var roundTripped = importer.ParseAll(output, "round-trip").ToList();

        Assert.Equal(5, source.Count);
        Assert.Equal(source.Count, roundTripped.Count);
        Assert.Equal(source.Select(c => c.DisplayName), roundTripped.Select(c => c.DisplayName));
        Assert.Equal(source.Select(c => c.Organization), roundTripped.Select(c => c.Organization));
        Assert.Equal(source.Select(c => c.Notes), roundTripped.Select(c => c.Notes));
        Assert.Equal(source.Select(c => c.Phones.Select(p => p.Digits).ToArray()),
            roundTripped.Select(c => c.Phones.Select(p => p.Digits).ToArray()));
        Assert.Equal(source.Select(c => c.Emails.Select(e => e.Address).ToArray()),
            roundTripped.Select(c => c.Emails.Select(e => e.Address).ToArray()));
        Assert.Equal(source.Select(c => c.Addresses.Count), roundTripped.Select(c => c.Addresses.Count));
        Assert.Equal(source.Select(c => c.Urls.ToArray()), roundTripped.Select(c => c.Urls.ToArray()));
    }

    private static string FindDemoVCard()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "samples", "demo.vcf");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate samples/demo.vcf from the test output directory.");
    }
}
