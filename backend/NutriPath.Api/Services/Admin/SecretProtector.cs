using System.Security.Cryptography;
using System.Text;

namespace NutriPath.Api.Services.Admin;

public sealed record ProtectedSecret(byte[] Ciphertext, byte[] Nonce, byte[] Tag, int KeyVersion);

/// <summary>
/// AES-256-GCM encryption for stored secrets (provider API keys, admin MFA seeds). Uses its own
/// versioned key, separate from the chat-message key:
///   Credentials:EncryptionKeys:{version} = base64(32 random bytes)
///   Credentials:ActiveKeyVersion        = {version}
/// Old versions stay configured so existing secrets can still be decrypted after a key rotation.
/// Every encryption uses a fresh random 96-bit nonce, and the associated data binds the ciphertext
/// to its purpose (e.g. "credential:Gemini:3"), so a value copied to another row fails to decrypt.
/// </summary>
public interface ISecretProtector
{
    bool Configured { get; }
    ProtectedSecret Protect(string plaintext, string associatedData);
    string Unprotect(ProtectedSecret secret, string associatedData);
}

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private readonly Dictionary<int, byte[]> _keys;
    private readonly int _activeVersion;

    public AesGcmSecretProtector(IConfiguration configuration)
    {
        _keys = new Dictionary<int, byte[]>();
        foreach (var child in configuration.GetSection("Credentials:EncryptionKeys").GetChildren())
        {
            if (!int.TryParse(child.Key, out var version) || string.IsNullOrWhiteSpace(child.Value)) continue;
            byte[] key;
            try
            {
                key = Convert.FromBase64String(child.Value);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"Credentials:EncryptionKeys:{version} is not valid base64.");
            }
            if (key.Length != 32)
                throw new InvalidOperationException($"Credentials:EncryptionKeys:{version} must decode to 32 bytes.");
            _keys[version] = key;
        }
        _activeVersion = configuration.GetValue("Credentials:ActiveKeyVersion", _keys.Count == 0 ? 0 : _keys.Keys.Max());
        if (_keys.Count > 0 && !_keys.ContainsKey(_activeVersion))
            throw new InvalidOperationException("Credentials:ActiveKeyVersion has no matching encryption key.");
    }

    public bool Configured => _keys.Count > 0;

    public ProtectedSecret Protect(string plaintext, string associatedData)
    {
        if (!Configured)
            throw new InvalidOperationException("Credential encryption is not configured (Credentials:EncryptionKeys).");
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[data.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(_keys[_activeVersion], tag.Length);
        aes.Encrypt(nonce, data, ciphertext, tag, Encoding.UTF8.GetBytes(associatedData));
        CryptographicOperations.ZeroMemory(data);
        return new ProtectedSecret(ciphertext, nonce, tag, _activeVersion);
    }

    public string Unprotect(ProtectedSecret secret, string associatedData)
    {
        if (!_keys.TryGetValue(secret.KeyVersion, out var key))
            throw new CryptographicException("The encryption key version for this secret is not configured.");
        var plaintext = new byte[secret.Ciphertext.Length];
        using var aes = new AesGcm(key, secret.Tag.Length);
        aes.Decrypt(secret.Nonce, secret.Ciphertext, secret.Tag, plaintext, Encoding.UTF8.GetBytes(associatedData));
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Single-string form (base64 of version|nonce|tag|ciphertext) for small values such as MFA seeds.</summary>
    public static string Pack(ProtectedSecret s) =>
        $"{s.KeyVersion}.{Convert.ToBase64String(s.Nonce)}.{Convert.ToBase64String(s.Tag)}.{Convert.ToBase64String(s.Ciphertext)}";

    public static ProtectedSecret Unpack(string packed)
    {
        var parts = packed.Split('.');
        if (parts.Length != 4 || !int.TryParse(parts[0], out var version))
            throw new CryptographicException("Malformed protected value.");
        return new ProtectedSecret(Convert.FromBase64String(parts[3]), Convert.FromBase64String(parts[1]),
            Convert.FromBase64String(parts[2]), version);
    }
}
