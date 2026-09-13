using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Security;

/// <summary>
/// Encrypts/decrypts key codes at rest with AES-256-GCM (fulfillment spec
/// "Key Encryption at Rest"; design section 4/8; ADR-05). Blob layout is
/// <c>nonce (12 bytes) | tag (16 bytes) | ciphertext</c>. Concrete class, no
/// interface (design decision — see design section 4.2). Keys are looked up by
/// version through a small dictionary so a future rotation (additional
/// <c>Keys:EncryptionKeys:{version}</c> entries) is additive; only
/// <see cref="KeyCipherOptions.CurrentVersion"/>'s key exists today.
/// </summary>
public sealed class KeyCipher
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly IReadOnlyDictionary<short, byte[]> _keysByVersion;
    private readonly short _currentVersion;

    public KeyCipher(IOptions<KeyCipherOptions> options)
    {
        var value = options.Value;

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(value.EncryptionKey);
        }
        catch (FormatException ex)
        {
            throw new KeyCipherException("Keys:EncryptionKey must be a valid base64 string.", ex);
        }

        if (keyBytes.Length != 32)
        {
            throw new KeyCipherException("Keys:EncryptionKey must decode to exactly 32 bytes.");
        }

        if (value.CurrentVersion < 1)
        {
            throw new KeyCipherException("Keys:CurrentVersion must be >= 1.");
        }

        _currentVersion = value.CurrentVersion;
        _keysByVersion = new Dictionary<short, byte[]> { [value.CurrentVersion] = keyBytes };
    }

    /// <summary>Encrypts <paramref name="plaintext"/> with the current key version and a fresh random nonce.</summary>
    public (byte[] Blob, short Version) Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var key = _keysByVersion[_currentVersion];
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);
        var tag = new byte[TagSizeBytes];
        var ciphertext = new byte[plaintextBytes.Length];

        using (var aes = new AesGcm(key, TagSizeBytes))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var blob = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, blob, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, blob, NonceSizeBytes + TagSizeBytes, ciphertext.Length);

        return (blob, _currentVersion);
    }

    /// <summary>
    /// Decrypts a blob produced by <see cref="Encrypt"/>. Throws <see cref="KeyCipherException"/>
    /// for an unknown <paramref name="version"/>, a malformed blob, or a tag/ciphertext
    /// mismatch — never a raw <see cref="CryptographicException"/> and never the plaintext
    /// or key material in the exception message.
    /// </summary>
    public string Decrypt(byte[] blob, short version)
    {
        ArgumentNullException.ThrowIfNull(blob);

        if (!_keysByVersion.TryGetValue(version, out var key))
        {
            throw new KeyCipherException($"Unknown key version {version}.");
        }

        if (blob.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new KeyCipherException("Malformed encrypted blob: too short for nonce and tag.");
        }

        var nonce = blob.AsSpan(0, NonceSizeBytes);
        var tag = blob.AsSpan(NonceSizeBytes, TagSizeBytes);
        var ciphertext = blob.AsSpan(NonceSizeBytes + TagSizeBytes);
        var plaintextBytes = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        }
        catch (CryptographicException ex)
        {
            throw new KeyCipherException("Failed to decrypt key: authentication tag verification failed.", ex);
        }

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
