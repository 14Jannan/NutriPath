using System.Security.Cryptography;
using System.Text;

namespace NutriPath.Api.Services;

/// <summary>Encrypts private text (chat messages) before it is stored, and decrypts it on the way out.</summary>
public interface IMessageProtector
{
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts a stored value. Values saved before encryption was added
    /// (no prefix) are returned unchanged. Throws CryptographicException if
    /// the value was tampered with or encrypted with a different key.
    /// </summary>
    string Unprotect(string stored);

    bool IsProtected(string stored);
}

/// <summary>
/// AES-256-GCM encryption for chat messages at rest. Why encryption and not
/// hashing: a hash can't be reversed, so history, search and the AI's memory
/// would all be impossible. Encryption keeps messages unreadable in the
/// database and in backups, while the app (which holds the key) can still
/// show them to their owner.
///
/// Stored format: "enc:v1:" + base64(nonce[12] + ciphertext + tag[16]).
/// A fresh random nonce per message means identical messages never produce
/// identical stored text, and GCM's tag makes any tampering detectable.
/// The "v1" leaves room to rotate to a new key or algorithm later.
/// </summary>
public class AesGcmMessageProtector : IMessageProtector
{
    public const string Prefix = "enc:v1:";
    private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagSize = 16;   // AesGcm.TagByteSizes.MaxSize

    private readonly byte[] _key;

    /// <param name="base64Key">A random 32-byte key, base64-encoded (from config, never the repo).</param>
    public AesGcmMessageProtector(string base64Key)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Encryption:MessageKey must be base64.");
        }
        if (key.Length != 32)
            throw new InvalidOperationException("Encryption:MessageKey must be a 32-byte (256-bit) key, base64-encoded.");
        _key = key;
    }

    public bool IsProtected(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + plainBytes.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        var cipher = output.AsSpan(NonceSize, plainBytes.Length);
        var tag = output.AsSpan(NonceSize + plainBytes.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return Prefix + Convert.ToBase64String(output);
    }

    public string Unprotect(string stored)
    {
        if (!IsProtected(stored)) return stored; // saved before encryption existed

        var data = Convert.FromBase64String(stored[Prefix.Length..]);
        if (data.Length < NonceSize + TagSize) throw new CryptographicException("Encrypted value is too short.");

        var nonce = data.AsSpan(0, NonceSize);
        var cipher = data.AsSpan(NonceSize, data.Length - NonceSize - TagSize);
        var tag = data.AsSpan(data.Length - TagSize, TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain); // throws if tampered or wrong key
        return Encoding.UTF8.GetString(plain);
    }
}
