using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public sealed class ExportReportTests
{
    [Fact]
    public void Reports_an_exact_vcard_round_trip()
    {
        var input = new Contact
        {
            Uid = "urn:uuid:round-trip",
            FormattedName = "Round Trip",
            GivenName = "Round",
            FamilyName = "Trip",
            Organization = "Acme",
            Title = "Engineer",
            Notes = "A note",
            Birthday = new DateOnly(1990, 1, 2),
        };
        input.Phones.Add(PhoneNumber.Parse("+15551234567", PhoneKind.Mobile, preferred: true));
        input.Emails.Add(new EmailAddress { Address = "round@example.com", Kind = EmailKind.Work });
        input.Categories.Add("Friends");
        input.Urls.Add("https://example.com");
        input.CustomFields["X-TEST"] = "value";

        var writer = new VCardWriter();
        var output = new VCardImporter().ParseAll(writer.Write(input)).ToList();

        var report = ExportReportComparer.Compare(new[] { input }, output);

        Assert.True(report.IsExact, report.Summary);
        Assert.Equal(0, report.DifferenceCount);
        Assert.Empty(report.Differences);
    }

    [Fact]
    public void Retains_a_bounded_detail_list_for_many_differences()
    {
        var input = Enumerable.Range(0, 150).Select(i => new Contact
        {
            FormattedName = $"Input {i}",
        }).ToList();
        var output = Enumerable.Range(0, 150).Select(i => new Contact
        {
            FormattedName = $"Output {i}",
        }).ToList();

        var report = ExportReportComparer.Compare(input, output);

        Assert.Equal(150, report.DifferenceCount);
        Assert.Equal(100, report.Differences.Count);
        Assert.False(report.IsExact);
    }
}
