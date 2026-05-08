using System.Text.Json;

namespace OrganizeContacts.Core;

/// <summary>User-facing options. Persisted as JSON next to the SQLite store.</summary>
public sealed class AppSettings
{
    public string DefaultRegion { get; set; } = "US";
    public string MatchProfile { get; set; } = "Default";  // Default | Strict | Loose

    // Email canonicalization toggles
    public bool MergeGoogleMailDomain { get; set; } = true;
    public bool StripGmailDots { get; set; } = true;
    public bool StripPlusTag { get; set; } = true;

    public bool ConfirmDestructiveActions { get; set; } = true;

    public static AppSettings LoadOrDefault(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
