using System.IO;
using OrganizeContacts.Core.Cleanup;
using OrganizeContacts.Core.Dedup;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;
using OrganizeContacts.Core.Normalize;

namespace OrganizeContacts.Cli;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 64;
    private const int ExitFail = 1;

    private static async Task<int> Main(string[] args)
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
        return args[0] switch
        {
            "import"  => await CmdImport(args[1..]),
            "export"  => await CmdExport(args[1..]),
            "dedupe"  => await CmdDedupe(args[1..]),
            "cleanup" => await CmdCleanup(args[1..]),
            "convert" => await CmdConvert(args[1..]),
            "version" or "--version" or "-v" => PrintVersion(),
            "help" or "--help" or "-h" => PrintUsage(),
            _ => PrintUsage(),
        };
    }

    private static int PrintVersion()
    {
        var v = typeof(Contact).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        Console.WriteLine($"oc {v}");
        return ExitOk;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            oc - OrganizeContacts headless tool

            Usage:
              oc convert <input> <output>          Read INPUT (vCard / Google CSV / Outlook CSV / LDIF / jCard) and write OUTPUT (vCard / Google CSV / Outlook CSV / jCard).
              oc dedupe  <input>...                 Print duplicate groups across one-or-more INPUT files (no writing).
              oc cleanup <input> <output>           Run intra-contact dedupe + normalize + canonicalize and write the cleaned contacts.
              oc version                            Print the version.
              oc help                               Print this message.

            Format detection is by file extension:
              .vcf / .vcard           vCard 2.1/3.0/4.0
              .csv                    Google CSV or Outlook CSV (auto-detected by header)
              .ldif                   Thunderbird/Mozilla LDIF
              .jcard / .jcf / .json   jCard (RFC 7095)
            """);
        return ExitUsage;
    }

    // ----- commands -----

    private static async Task<int> CmdConvert(string[] args)
    {
        if (args.Length != 2) return PrintUsage();
        var inputPath = args[0];
        var outputPath = args[1];

        var input = await ReadAllAsync(inputPath);
        await WriteAllAsync(outputPath, input);
        Console.WriteLine($"converted {input.Count} contact(s) -> {outputPath}");
        return ExitOk;
    }

    private static async Task<int> CmdDedupe(string[] args)
    {
        if (args.Length == 0) return PrintUsage();
        var all = new List<Contact>();
        foreach (var p in args) all.AddRange(await ReadAllAsync(p));

        var engine = new DedupEngine();
        var groups = engine.Find(all);
        Console.WriteLine($"{groups.Count} duplicate group(s) across {all.Count} contact(s):");
        foreach (var g in groups)
        {
            Console.WriteLine($"  [{g.Confidence:P0}] {g.MatchReason} - {g.Members.Count}");
            foreach (var m in g.Members)
                Console.WriteLine($"      - {m.DisplayName}  ({m.SourceFile})");
        }
        return ExitOk;
    }

    private static async Task<int> CmdCleanup(string[] args)
    {
        if (args.Length != 2) return PrintUsage();
        var inputPath = args[0];
        var outputPath = args[1];

        var contacts = (await ReadAllAsync(inputPath)).ToList();
        var report = new BatchCleanup(new PhoneNormalizer(), new EmailCanonicalizer())
            .Run(contacts);
        Console.WriteLine(report.Summary);

        await WriteAllAsync(outputPath, contacts);
        Console.WriteLine($"wrote {contacts.Count} -> {outputPath}");
        return ExitOk;
    }

    private static async Task<int> CmdImport(string[] args)
    {
        if (args.Length == 0) return PrintUsage();
        var contacts = await ReadAllAsync(args[0]);
        Console.WriteLine($"read {contacts.Count} contact(s) from {args[0]}");
        return ExitOk;
    }

    private static async Task<int> CmdExport(string[] args)
    {
        if (args.Length != 2) return PrintUsage();
        // Treat as alias for convert
        return await CmdConvert(args);
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
}
