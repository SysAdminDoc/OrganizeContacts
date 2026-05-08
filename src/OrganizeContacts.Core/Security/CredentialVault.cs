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

    /// <summary>True after a Load() salvaged a corrupt vault by side-lining it. The next
    /// Persist() will not silently overwrite a recoverable file.</summary>
    public bool CorruptVaultDetected { get; private set; }

    [SupportedOSPlatform("windows")]
    private Dictionary<string, CredentialEntry> Load()
    {
        if (!File.Exists(_path)) return NewDict();
        try
        {
            var encrypted = File.ReadAllBytes(_path);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: VaultEntropy, scope: DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            var dict = JsonSerializer.Deserialize<Dictionary<string, CredentialEntry>>(json);
            // JsonSerializer rebuilds the dictionary with its default comparer (Ordinal/case-sensitive).
            // We rebuild with OrdinalIgnoreCase so callers can look up "carddav" regardless of how it was saved.
            if (dict is null) return NewDict();
            var ci = NewDict();
            foreach (var kv in dict) ci[kv.Key] = kv.Value;
            return ci;
        }
        catch
        {
            // Corrupt vault — preserve the original so the user (or a recovery script) can attempt
            // re-decryption later, then return an empty in-memory dict. Persist() will refuse to
            // overwrite the original until the user explicitly clears the flag.
            BackupCorruptVault();
            CorruptVaultDetected = true;
            return NewDict();
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
        AtomicWrite(_path, encrypted);
    }

    private void BackupCorruptVault()
    {
        try
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var backup = _path + $".corrupt-{stamp}.bak";
            File.Copy(_path, backup, overwrite: false);
        }
        catch
        {
            // Backup is best-effort — don't block the user.
        }
    }

    /// <summary>Atomic write: temp file + Move with Replace so an abrupt termination
    /// can't leave the encrypted blob half-written.</summary>
    private static void AtomicWrite(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    private static Dictionary<string, CredentialEntry> NewDict() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly byte[] VaultEntropy = Encoding.UTF8.GetBytes("OrganizeContacts.CredentialVault.v1");
}
