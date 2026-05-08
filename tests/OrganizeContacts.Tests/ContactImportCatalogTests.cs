using System.IO;
using OrganizeContacts.Core.Importers;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Tests;

public sealed class ContactImportCatalogTests
{
    [Fact]
    public void Folder_scan_detects_supported_contact_files_only()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oc-folder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.vcf"),
                "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:A Person\r\nEND:VCARD\r\n");
            File.WriteAllText(Path.Combine(root, "google.csv"),
                "Name,Given Name,Family Name,Organization Name,E-mail 1 - Value\nA,A,Person,Acme,a@example.com\n");
            File.WriteAllText(Path.Combine(root, "outlook.csv"),
                "First Name,Last Name,Company,E-mail Address\nB,Person,Acme,b@example.com\n");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "not a contact file");

            var files = BuildCatalog().FindFiles(root);

            Assert.Equal(3, files.Count);
            Assert.Contains(files, f => Path.GetFileName(f.FilePath) == "a.vcf" &&
                                        f.Format.SourceKind == SourceKind.File);
            Assert.Contains(files, f => Path.GetFileName(f.FilePath) == "google.csv" &&
                                        f.Format.SourceKind == SourceKind.GoogleCsv);
            Assert.Contains(files, f => Path.GetFileName(f.FilePath) == "outlook.csv" &&
                                        f.Format.SourceKind == SourceKind.OutlookCsv);
            Assert.DoesNotContain(files, f => Path.GetFileName(f.FilePath) == "notes.txt");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Folder_scan_is_not_recursive_unless_requested()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oc-folder-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllText(Path.Combine(nested, "nested.vcf"),
                "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Nested Person\r\nEND:VCARD\r\n");

            Assert.Empty(BuildCatalog().FindFiles(root));
            Assert.Single(BuildCatalog().FindFiles(root, recursive: true));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static ContactImportCatalog BuildCatalog() => new(new[]
    {
        new ContactImportFormat(new VCardImporter(), SourceKind.File),
        new ContactImportFormat(new GoogleCsvImporter(), SourceKind.GoogleCsv),
        new ContactImportFormat(new OutlookCsvImporter(), SourceKind.OutlookCsv),
        new ContactImportFormat(new LdifImporter(), SourceKind.Thunderbird),
        new ContactImportFormat(new JCardImporter(), SourceKind.File),
    });
}
