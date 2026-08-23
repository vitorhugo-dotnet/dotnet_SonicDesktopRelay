using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SonicDesktopRelay.Core.Identity;

/// <summary>
/// Stores the device credential in one file under the user's profile, encrypted with DPAPI at
/// <see cref="DataProtectionScope.CurrentUser"/>. Another user on the same machine cannot
/// decrypt it, and a copied file is useless elsewhere.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileDeviceCredentialStore(string filePath) : IDeviceCredentialStore
{
    // Bound to this application so a blob lifted from another DPAPI-using app cannot be fed in.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SonicDesktopRelay.DeviceCredential.v1");

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SonicDesktopRelay", "device.bin");

    public async Task<DeviceCredential?> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(filePath, ct);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<DeviceCredential>(plain);
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            // A file we cannot decrypt or parse is indistinguishable from no identity at all:
            // the caller's only sane response either way is to bootstrap a new device. Throwing
            // here would strand the app on a corrupt file with no path forward.
            return null;
        }
    }

    public async Task WriteAsync(DeviceCredential credential, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(credential);
        var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(filePath, protectedBytes, ct);
    }

    public Task ClearAsync(CancellationToken ct)
    {
        if (File.Exists(filePath)) File.Delete(filePath);
        return Task.CompletedTask;
    }
}
