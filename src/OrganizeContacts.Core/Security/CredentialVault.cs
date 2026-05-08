using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrganizeContacts.Core.Security;

public sealed record CredentialEntry(string Account, string Username, string Secret, DateTimeOffset CreatedAt);

/// <summary>
/// DPAPI-backed credential store. Secrets are encrypted with the current Windows user's
/// data-protection key (CurrentUser scope) and persisted as a JSON blob next to the SQLite DB.
/// On non-Windows hosts the API throws — callers should guard with <see cref="IsSupported"/>.
/// </summary>
public sealed class CredentialVault
{
    private readonly string _path;

    public CredentialVault(string path) => _path = path;

    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [SupportedOSPlatform("windows")]
    public IReadOnlyList<CredentialEntry> List() =>
        Load().Values.OrderBy(e => e.Account, StringComparer.OrdinalIgnoreCase).ToList();

    [SupportedOSPlatform("windows")]
    public CredentialEntry? Get(string account)
    {
        var dict = Load();
        return dict.TryGetValue(account, out var entry) ? entry : null;
    }

    [SupportedOSPlatform("windows")]
    public void Save(string account, string username, string secret)
    {
        var dict = Load();
        dict[account] = new CredentialEntry(account, username, secret, DateTimeOffset.UtcNow);
        Persist(dict);
    }

    [SupportedOSPlatform("windows")]
    public bool Delete(string account)
    {
        var dict = Load();
        if (!dict.Remove(account)) return false;
        Persist(dict);
        return true;
    }

    [SupportedOSPlatform("windows")]
    private Dictionary<string, CredentialEntry> Load()
    {
        if (!File.Exists(_path)) return new Dictionary<string, CredentialEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var encrypted = File.ReadAllBytes(_path);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: VaultEntropy, scope: DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            var dict = JsonSerializer.Deserialize<Dictionary<string, CredentialEntry>>(json);
            return dict ?? new Dictionary<string, CredentialEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Corrupt vault — start over rather than block the user. Audit lives elsewhere.
            return new Dictionary<string, CredentialEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    [SupportedOSPlatform("windows")]
    private void Persist(Dictionary<string, CredentialEntry> dict)
    {
        var json = JsonSerializer.Serialize(dict);
        var bytes = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: VaultEntropy, scope: DataProtectionScope.CurrentUser);
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(_path, encrypted);
    }

    private static readonly byte[] VaultEntropy = Encoding.UTF8.GetBytes("OrganizeContacts.CredentialVault.v1");
}
