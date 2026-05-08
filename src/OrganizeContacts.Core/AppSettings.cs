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

    /// <summary>"Mocha" (default dark) or "Latte" (light).</summary>
    public string Theme { get; set; } = "Mocha";

    /// <summary>Set by <see cref="LoadOrDefault"/> if the on-disk file existed but
    /// could not be parsed and the file was side-lined for inspection.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? LoadError { get; private set; }

    public static AppSettings LoadOrDefault(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded is null) return new AppSettings { LoadError = "settings file decoded to null" };
            // Defensive: missing/empty region falls back to US instead of attempting
            // to call PhoneNormalizer with whitespace.
            if (string.IsNullOrWhiteSpace(loaded.DefaultRegion)) loaded.DefaultRegion = "US";
            if (string.IsNullOrWhiteSpace(loaded.MatchProfile)) loaded.MatchProfile = "Default";
            if (string.IsNullOrWhiteSpace(loaded.Theme)) loaded.Theme = "Mocha";
            return loaded;
        }
        catch (Exception ex)
        {
            // Side-line the unreadable file so the user (or support) can inspect it,
            // then fall back to defaults — never block startup.
            try { File.Copy(path, path + ".invalid.bak", overwrite: true); } catch { }
            return new AppSettings { LoadError = ex.Message };
        }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        // Atomic write: a crash mid-WriteAllText would otherwise leave a truncated file.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
