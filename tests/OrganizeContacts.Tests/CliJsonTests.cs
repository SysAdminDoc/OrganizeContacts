using System.Text.Json;
using OrganizeContacts.Cli;

namespace OrganizeContacts.Tests;

public sealed class CliJsonTests
{
    [Fact]
    public async Task Dedupe_json_output_is_machine_readable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-cli-{Guid.NewGuid():N}.vcf");
        await File.WriteAllTextAsync(path, """
            BEGIN:VCARD
            VERSION:3.0
            FN:Jane Doe
            EMAIL:jane@example.com
            END:VCARD
            BEGIN:VCARD
            VERSION:3.0
            FN:Jane Doe
            EMAIL:jane@example.com
            END:VCARD
            """);

        var originalOut = Console.Out;
        var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var exitCode = await Program.RunAsync(new[] { "dedupe", "--json", path });

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            var root = document.RootElement;
            Assert.Equal("dedupe", root.GetProperty("command").GetString());
            Assert.Equal(2, root.GetProperty("contactCount").GetInt32());
            Assert.Equal(1, root.GetProperty("duplicateGroupCount").GetInt32());
            Assert.Equal(2, root.GetProperty("groups")[0].GetProperty("members").GetArrayLength());
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
            try { File.Delete(path); } catch { }
        }
    }
}
