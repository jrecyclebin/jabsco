using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Jabsco.Core.Platform;

namespace Jabsco.Core.Credentials;

// Stores credentials as DPAPI-encrypted JSON files in the state directory.
// Each credential is a separate file keyed by a sanitized credentialRef.
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
{
    private readonly string _dir;

    public DpapiCredentialStore(string? storeDir = null)
    {
        _dir = storeDir ?? Path.Combine(KnownPaths.StateDir, "credentials");
        Directory.CreateDirectory(_dir);
    }

    public Task<NetworkCredential?> GetAsync(string credentialRef, CancellationToken ct)
    {
        var path = CredPath(credentialRef);
        if (!File.Exists(path)) return Task.FromResult<NetworkCredential?>(null);

        var encrypted = Convert.FromBase64String(File.ReadAllText(path));
        var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return Task.FromResult<NetworkCredential?>(new NetworkCredential(
            root.GetProperty("username").GetString() ?? string.Empty,
            root.GetProperty("password").GetString() ?? string.Empty,
            root.TryGetProperty("domain", out var d) ? d.GetString() : null));
    }

    public Task SetAsync(string credentialRef, NetworkCredential credential, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            username = credential.UserName,
            password = credential.Password,
            domain = credential.Domain
        });
        var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
        File.WriteAllText(CredPath(credentialRef), Convert.ToBase64String(encrypted));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string credentialRef, CancellationToken ct)
    {
        var path = CredPath(credentialRef);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string CredPath(string credentialRef)
    {
        var safe = string.Concat(credentialRef.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_dir, safe + ".cred");
    }
}
