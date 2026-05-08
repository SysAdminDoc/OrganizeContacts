using System.IO;
using System.Runtime.InteropServices;
using OrganizeContacts.Core.Security;

namespace OrganizeContacts.Tests;

public class CredentialVaultTests
{
    [Fact]
    public void Round_trips_encrypted_credential()
    {
        if (!CredentialVault.IsSupported) return; // skip on non-Windows CI

        var path = Path.Combine(Path.GetTempPath(), $"oc-vault-{Guid.NewGuid():N}.dat");
        try
        {
            var v = new CredentialVault(path);
            v.Save("nextcloud", "matt", "s3cr3t");
            var entries = v.List();
            Assert.Single(entries);
            var entry = v.Get("nextcloud");
            Assert.NotNull(entry);
            Assert.Equal("matt", entry!.Username);
            Assert.Equal("s3cr3t", entry.Secret);

            // Confirm bytes on disk are not plaintext.
            var raw = File.ReadAllBytes(path);
            Assert.DoesNotContain((byte)'s', raw.SkipWhile(b => b != 0).Take(20).ToArray());
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Delete_removes_entry()
    {
        if (!CredentialVault.IsSupported) return;
        var path = Path.Combine(Path.GetTempPath(), $"oc-vault-{Guid.NewGuid():N}.dat");
        try
        {
            var v = new CredentialVault(path);
            v.Save("a", "u", "s");
            Assert.NotNull(v.Get("a"));
            Assert.True(v.Delete("a"));
            Assert.Null(v.Get("a"));
            Assert.False(v.Delete("a"));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
