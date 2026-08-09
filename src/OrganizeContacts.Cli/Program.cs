using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizeContacts.Core.Cleanup;
using OrganizeContacts.Core.Dedup;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Normalize;

namespace OrganizeContacts.Cli;

public static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 64;
    private const int ExitFail = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> Main(string[] args)
        => await RunAsync(args);

    /// <summary>Runs the CLI command dispatcher, including its exit-code handling.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return args.Length == 0
                ? PrintUsage()
                : await Dispatch(args);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"oc: {ex.Message}");
            return ExitFail;
        }
    }

    private static async Task<int> Dispatch(string[] args)
    {
        var json = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
        var commandArgs = args
            .Where(a => !string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (commandArgs.Length == 0) return PrintUsage();

        return commandArgs[0] switch
        {
            "import"  => await CmdImport(commandArgs[1..], json),
            "export"  => await CmdExport(commandArgs[1..], json),
            "dedupe"  => await CmdDedupe(commandArgs[1..], json),
            "cleanup" => await CmdCleanup(commandArgs[1..], json),
            "convert" => await CmdConvert(commandArgs[1..], json),
            "version" or "--version" or "-v" => PrintVersion(json),
            "help" or "--help" or "-h" => PrintUsage(),
            _ => PrintUsage(),
        };
    }

    private static int PrintVersion(bool json)
    {
        var v = typeof(Contact).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        if (json)
            WriteJson(new { command = "version", version = v });
        else
            Console.WriteLine($"oc {v}");
        return ExitOk;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            oc - OrganizeContacts headless tool

            Usage:
              oc import  <input>                  Read INPUT and report its contact count.
              oc convert <input> <output>          Read INPUT (vCard / Google CSV / Outlook CSV / LDIF / jCard) and write OUTPUT (vCard / Google CSV / Outlook CSV / jCard / iCalendar).
              oc dedupe  <input>...                 Print duplicate groups across one-or-more INPUT files (no writing).
              oc cleanup <input> <output>           Run intra-contact dedupe + normalize + canonicalize and write the cleaned contacts.
              oc version                            Print the version.
              oc help                               Print this message.

            Add --json to import, convert/export, dedupe, cleanup, or version for indented machine-readable output.

            Format detection is by file extension:
              .vcf / .vcard           vCard 2.1/3.0/4.0
              .csv                    Google CSV or Outlook CSV (auto-detected by header)
              .ldif                   Thunderbird/Mozilla LDIF
              .jcard / .jcf / .json   jCard (RFC 7095)
            """);
        return ExitUsage;
    }

    // ----- commands -----

    private static async Task<int> CmdConvert(string[] args, bool json)
    {
        if (args.Length != 2) return PrintUsage();
        var inputPath = args[0];
        var outputPath = args[1];

        var input = await ReadAllAsync(inputPath);
        await WriteAllAsync(outputPath, input);
        if (json)
            WriteJson(new { command = "convert", input = inputPath, output = outputPath, contacts = input.Count });
        else
            Console.WriteLine($"converted {input.Count} contact(s) -> {outputPath}");
        return ExitOk;
    }

    private static async Task<int> CmdDedupe(string[] args, bool json)
    {
        if (args.Length == 0) return PrintUsage();
        var all = new List<Contact>();
        foreach (var p in args) all.AddRange(await ReadAllAsync(p));

        var engine = new DedupEngine();
        var groups = engine.Find(all);

        if (json)
        {
            WriteJson(new
            {
                command = "dedupe",
                contactCount = all.Count,
                duplicateGroupCount = groups.Count,
                groups = groups.Select(g => new
                {
                    id = g.Id,
                    confidence = g.Confidence,
                    matchReason = g.MatchReason,
                    signals = g.Signals.Select(s => new { s.Label, s.Weight, s.Detail }),
                    members = g.Members.Select(c => new
                    {
                        id = c.Id,
                        c.Uid,
                        displayName = c.DisplayName,
                        c.SourceFile,
                        c.SourceFormat,
                    }),
                }),
            });
            return ExitOk;
        }

        Console.WriteLine($"{groups.Count} duplicate group(s) across {all.Count} contact(s):");
        foreach (var g in groups)
        {
            Console.WriteLine($"  [{g.Confidence:P0}] {g.MatchReason} - {g.Members.Count}");
            foreach (var m in g.Members)
                Console.WriteLine($"      - {m.DisplayName}  ({m.SourceFile})");
        }
        return ExitOk;
    }

    private static async Task<int> CmdCleanup(string[] args, bool json)
    {
        if (args.Length != 2) return PrintUsage();
        var inputPath = args[0];
        var outputPath = args[1];

        var contacts = (await ReadAllAsync(inputPath)).ToList();
        var report = new BatchCleanup(new PhoneNormalizer(), new EmailCanonicalizer())
            .Run(contacts);

        await WriteAllAsync(outputPath, contacts);
        if (json)
            WriteJson(new
            {
                command = "cleanup",
                input = inputPath,
                output = outputPath,
                contacts = contacts.Count,
                summary = report.Summary,
                report,
            });
        else
        {
            Console.WriteLine(report.Summary);
            Console.WriteLine($"wrote {contacts.Count} -> {outputPath}");
        }
        return ExitOk;
    }

    private static async Task<int> CmdImport(string[] args, bool json)
    {
        if (args.Length == 0) return PrintUsage();
        var contacts = await ReadAllAsync(args[0]);
        if (json)
            WriteJson(new { command = "import", input = args[0], contacts = contacts.Count, records = contacts });
        else
            Console.WriteLine($"read {contacts.Count} contact(s) from {args[0]}");
        return ExitOk;
    }

    private static async Task<int> CmdExport(string[] args, bool json)
    {
        if (args.Length != 2) return PrintUsage();
        // Treat as alias for convert
        return await CmdConvert(args, json);
    }

    // ----- helpers -----

    private static async Task<List<Contact>> ReadAllAsync(string path)
    {
        IContactImporter importer = ImporterFor(path);
        var list = new List<Contact>();
        await foreach (var c in importer.ReadAsync(path)) list.Add(c);
        return list;
    }

    private static IContactImporter ImporterFor(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".vcf" or ".vcard" => new VCardImporter(),
            ".ldif" => new LdifImporter(),
            ".jcard" or ".jcf" or ".json" => new JCardImporter(),
            ".csv" => DetectCsvImporter(path),
            _ => throw new InvalidOperationException($"unrecognised input extension: {ext}"),
        };
    }

    private static IContactImporter DetectCsvImporter(string path)
    {
        var google = new GoogleCsvImporter();
        if (google.CanRead(path)) return google;
        var outlook = new OutlookCsvImporter();
        if (outlook.CanRead(path)) return outlook;
        return google; // best guess
    }

    private static async Task WriteAllAsync(string path, IReadOnlyList<Contact> contacts)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".vcf":
            case ".vcard":
                await new VCardWriter().WriteFileAsync(path, contacts);
                break;
            case ".jcard":
            case ".jcf":
            case ".json":
                await new JCardWriter().WriteFileAsync(path, contacts);
                break;
            case ".ics":
            case ".ical":
                await new IcsWriter().WriteFileAsync(path, contacts);
                break;
            case ".csv" when path.IndexOf("outlook", StringComparison.OrdinalIgnoreCase) >= 0:
                await new OutlookCsvWriter().WriteFileAsync(path, contacts);
                break;
            case ".csv":
                await new GoogleCsvWriter().WriteFileAsync(path, contacts);
                break;
            default:
                throw new InvalidOperationException($"unrecognised output extension: {ext}");
        }
    }

    private static void WriteJson<T>(T value)
        => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
}
