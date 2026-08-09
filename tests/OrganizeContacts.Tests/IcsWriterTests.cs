using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public sealed class IcsWriterTests
{
    [Fact]
    public void Writes_yearly_birthday_and_anniversary_events()
    {
        var contact = new Contact
        {
            Uid = "person-1",
            FormattedName = "Ada, Lovelace",
            Birthday = new DateOnly(1815, 12, 10),
            Anniversary = new DateOnly(1843, 7, 8),
            Notes = "Remember, with a comma; and a newline\nhere.",
        };

        var output = new IcsWriter().Write(contact);

        Assert.Equal(2, output.Split("BEGIN:VEVENT", StringSplitOptions.None).Length - 1);
        Assert.Contains("DTSTART;VALUE=DATE:18151210", output);
        Assert.Contains("DTSTART;VALUE=DATE:18430708", output);
        Assert.Equal(2, output.Split("RRULE:FREQ=YEARLY", StringSplitOptions.None).Length - 1);
        Assert.Contains("SUMMARY:Ada\\, Lovelace — Birthday", output);
        Assert.Contains("DESCRIPTION:Remember\\, with a comma\\; and a newline\\nhere.", output);
        Assert.Contains("CATEGORIES:ANNIVERSARY", output);
    }

    [Fact]
    public void Omits_contacts_without_calendar_dates()
    {
        var output = new IcsWriter().Write(new Contact { FormattedName = "No date" });

        Assert.DoesNotContain("BEGIN:VEVENT", output);
        Assert.Contains("BEGIN:VCALENDAR", output);
        Assert.Contains("END:VCALENDAR", output);
    }

    [Fact]
    public void Folds_long_utf8_lines_without_exceeding_75_octets()
    {
        var contact = new Contact
        {
            FormattedName = new string('é', 100),
            Birthday = new DateOnly(2000, 1, 2),
        };

        var output = new IcsWriter().Write(contact);
        foreach (var line in output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75, $"Line was too long: {line}");
    }
}
