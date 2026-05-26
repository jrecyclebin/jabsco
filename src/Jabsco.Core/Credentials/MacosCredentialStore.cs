using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jabsco.Core.Platform;

namespace Jabsco.Core.Credentials;

// AES-encrypted file credential store for macOS.
// Uses the system UUID (via sysctl) as the machine-scoped key.
[SupportedOSPlatform("macos")]
public sealed class MacosCredentialStore : ICredentialStore
{
    private readonly string _dir;
    private readonly byte[] _key;

    public MacosCredentialStore(string? storeDir = null)
    {
        _dir = storeDir ?? Path.Combine(KnownPaths.StateDir, "credentials");
        Directory.CreateDirectory(_dir);
        _key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(GetMachineId()),
            Encoding.UTF8.GetBytes("jabsco-cred-salt"),
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
    }

    public Task<NetworkCredential?> GetAsync(string credentialRef, CancellationToken ct)
    {
        var path = CredPath(credentialRef);
        if (!File.Exists(path)) return Task.FromResult<NetworkCredential?>(null);

        var decrypted = DecryptAes(File.ReadAllBytes(path));
        var doc = JsonDocument.Parse(decrypted);
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
            domain   = credential.Domain
        });
        File.WriteAllBytes(CredPath(credentialRef), EncryptAes(json));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string credentialRef, CancellationToken ct)
    {
        var path = CredPath(credentialRef);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private byte[] EncryptAes(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            cs.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private byte[] DecryptAes(byte[] blob)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = blob[..16];
        using var ms = new MemoryStream(blob[16..]);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var result = new MemoryStream();
        cs.CopyTo(result);
        return result.ToArray();
    }

    private string CredPath(string credentialRef)
    {
        var safe = string.Concat(credentialRef.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_dir, safe + ".cred");
    }

    private static string GetMachineId()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("sysctl", "-n hw.uuid")
                { RedirectStandardOutput = true, UseShellExecute = false };
            using var proc = System.Diagnostics.Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit();
            if (!string.IsNullOrEmpty(output)) return output;
        }
        catch { }
        return Environment.MachineName;
    }
}
