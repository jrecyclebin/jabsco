using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jabsco.Core.Platform;

namespace Jabsco.Core.Credentials;

// Linux credential store. Attempts to use the org.freedesktop.secrets D-Bus API
// (e.g. GNOME Keyring, KWallet). Falls back to PBKDF2-encrypted files if unavailable.
[SupportedOSPlatform("linux")]
public sealed class LibsecretCredentialStore : ICredentialStore
{
    private const string Collection = "jabsco";
    private readonly string _fallbackDir;
    private readonly byte[] _fallbackKey;

    public LibsecretCredentialStore(string? storeDir = null)
    {
        _fallbackDir = storeDir ?? Path.Combine(KnownPaths.StateDir, "credentials");
        Directory.CreateDirectory(_fallbackDir);

        // Derive a machine-scoped key for fallback encryption from a stable secret.
        // Uses the machine ID as entropy — good enough for at-rest protection.
        var machineId = GetMachineId();
        _fallbackKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(machineId),
            Encoding.UTF8.GetBytes("jabsco-cred-salt"),
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
    }

    public async Task<NetworkCredential?> GetAsync(string credentialRef, CancellationToken ct)
    {
        // TODO: Try libsecret via D-Bus first.
        // org.freedesktop.secrets.Service.SearchItems({{"jabsco:ref", credentialRef}})
        // then Item.GetSecret() — requires Tmds.DBus.Protocol message construction.
        // For now, use encrypted file fallback.

        return await GetFromFileAsync(credentialRef, ct);
    }

    public async Task SetAsync(string credentialRef, NetworkCredential credential, CancellationToken ct)
    {
        // TODO: Store via libsecret D-Bus when available.
        await SetToFileAsync(credentialRef, credential, ct);
    }

    public Task DeleteAsync(string credentialRef, CancellationToken ct)
    {
        var path = CredPath(credentialRef);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private Task<NetworkCredential?> GetFromFileAsync(string credentialRef, CancellationToken ct)
    {
        var path = CredPath(credentialRef);
        if (!File.Exists(path)) return Task.FromResult<NetworkCredential?>(null);

        var blob = File.ReadAllBytes(path);
        var decrypted = DecryptAes(blob);
        var doc = JsonDocument.Parse(decrypted);
        var root = doc.RootElement;

        return Task.FromResult<NetworkCredential?>(new NetworkCredential(
            root.GetProperty("username").GetString() ?? string.Empty,
            root.GetProperty("password").GetString() ?? string.Empty,
            root.TryGetProperty("domain", out var d) ? d.GetString() : null));
    }

    private Task SetToFileAsync(string credentialRef, NetworkCredential credential, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            username = credential.UserName,
            password = credential.Password,
            domain = credential.Domain
        });
        var encrypted = EncryptAes(json);
        File.WriteAllBytes(CredPath(credentialRef), encrypted);
        return Task.CompletedTask;
    }

    private byte[] EncryptAes(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = _fallbackKey;
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
        aes.Key = _fallbackKey;
        var iv = blob[..16];
        var ciphertext = blob[16..];
        aes.IV = iv;
        using var ms = new MemoryStream(ciphertext);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var result = new MemoryStream();
        cs.CopyTo(result);
        return result.ToArray();
    }

    private string CredPath(string credentialRef)
    {
        var safe = string.Concat(credentialRef.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_fallbackDir, safe + ".cred");
    }

    private static string GetMachineId()
    {
        // /etc/machine-id is present on most Linux distros (systemd)
        const string machineIdPath = "/etc/machine-id";
        if (File.Exists(machineIdPath))
            return File.ReadAllText(machineIdPath).Trim();
        return Environment.MachineName;
    }
}
