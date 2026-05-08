using System.IO;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Importers;

public sealed record ContactImportFormat(IContactImporter Importer, SourceKind SourceKind);

public sealed record DetectedContactFile(string FilePath, ContactImportFormat Format);

public sealed class ContactImportCatalog
{
    private readonly IReadOnlyList<ContactImportFormat> _formats;

    public ContactImportCatalog(IEnumerable<ContactImportFormat> formats)
    {
        _formats = formats.ToList();
    }

    public IReadOnlyList<DetectedContactFile> FindFiles(string folderPath, bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return Array.Empty<DetectedContactFile>();

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(folderPath, "*", option)
            .Select(path => (path, format: Detect(path)))
            .Where(x => x.format is not null)
            .Select(x => new DetectedContactFile(x.path, x.format!))
            .OrderBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ContactImportFormat? Detect(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return DetectCsv(filePath);

        return _formats.FirstOrDefault(f =>
            f.Importer.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) &&
            f.Importer.CanRead(filePath));
    }

    private ContactImportFormat? DetectCsv(string filePath)
    {
        var csvFormats = _formats
            .Where(f => f.Importer.SupportedExtensions.Contains(".csv", StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (csvFormats.Count == 0) return null;

        string header;
        try
        {
            header = File.ReadLines(filePath).FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return null;
        }

        var google = csvFormats.FirstOrDefault(f =>
            f.SourceKind == SourceKind.GoogleCsv ||
            f.Importer.Name.Contains("Google", StringComparison.OrdinalIgnoreCase));
        var outlook = csvFormats.FirstOrDefault(f =>
            f.SourceKind == SourceKind.OutlookCsv ||
            f.Importer.Name.Contains("Outlook", StringComparison.OrdinalIgnoreCase));

        if (google is not null &&
            ContainsAny(header, "E-mail 1 - Value", "Phone 1 - Value", "Organization Name", "Group Membership") &&
            google.Importer.CanRead(filePath))
            return google;

        if (outlook is not null &&
            ContainsAny(header, "E-mail Address", "Business Phone", "Home Phone", "First Name", "Last Name", "Company") &&
            outlook.Importer.CanRead(filePath))
            return outlook;

        return csvFormats.FirstOrDefault(f => f.Importer.CanRead(filePath));
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));
}
