using Maxkeys.Application.Security;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Security;

/// <summary>
/// Covers <see cref="KeyCipher"/> round-trip, tamper detection, and version
/// handling (fulfillment spec "Key Encryption at Rest"; tasks.md 4.3). No
/// Postgres dependency — deliberately outside <c>PostgresCollection</c>.
/// </summary>
public class KeyCipherTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static KeyCipher CreateCipher(string keyBase64 = "", short currentVersion = 1)
    {
        return new KeyCipher(Options.Create(new KeyCipherOptions
        {
            EncryptionKey = keyBase64.Length == 0 ? ValidKeyBase64 : keyBase64,
            CurrentVersion = currentVersion,
        }));
    }

    [Fact]
    public void Encrypt_then_decrypt_returns_original_plaintext()
    {
        var cipher = CreateCipher();

        var (blob, version) = cipher.Encrypt("super-secret-key-code");
        var decrypted = cipher.Decrypt(blob, version);

        Assert.Equal("super-secret-key-code", decrypted);
    }

    [Fact]
    public void Encrypt_returns_blob_that_is_not_the_plaintext_bytes_and_has_expected_length()
    {
        var cipher = CreateCipher();
        const string plaintext = "ABCDE-12345-FGHIJ";

        var (blob, _) = cipher.Encrypt(plaintext);

        Assert.Equal(12 + 16 + System.Text.Encoding.UTF8.GetByteCount(plaintext), blob.Length);
        Assert.NotEqual(System.Text.Encoding.UTF8.GetBytes(plaintext), blob);
    }

    [Fact]
    public void Encrypt_called_twice_with_same_plaintext_produces_different_blobs()
    {
        var cipher = CreateCipher();

        var (blobA, _) = cipher.Encrypt("same-code");
        var (blobB, _) = cipher.Encrypt("same-code");

        Assert.NotEqual(blobA, blobB);
    }

    [Fact]
    public void Decrypt_with_tampered_tag_throws_KeyCipherException()
    {
        var cipher = CreateCipher();
        var (blob, version) = cipher.Encrypt("tamper-tag-target");
        blob[12] ^= 0xFF; // tag starts at offset 12

        Assert.Throws<KeyCipherException>(() => cipher.Decrypt(blob, version));
    }

    [Fact]
    public void Decrypt_with_tampered_ciphertext_throws_KeyCipherException()
    {
        var cipher = CreateCipher();
        var (blob, version) = cipher.Encrypt("tamper-ciphertext-target");
        blob[^1] ^= 0xFF; // last ciphertext byte

        Assert.Throws<KeyCipherException>(() => cipher.Decrypt(blob, version));
    }

    [Fact]
    public void Decrypt_with_unknown_version_throws_KeyCipherException()
    {
        var cipher = CreateCipher(currentVersion: 1);
        var (blob, _) = cipher.Encrypt("some-code");

        Assert.Throws<KeyCipherException>(() => cipher.Decrypt(blob, 2));
    }

    [Fact]
    public void Options_validation_rejects_a_16_byte_key()
    {
        var validator = new KeyCipherOptionsValidator();
        var options = new KeyCipherOptions
        {
            EncryptionKey = Convert.ToBase64String(new byte[16]),
            CurrentVersion = 1,
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Options_validation_rejects_a_non_base64_string()
    {
        var validator = new KeyCipherOptionsValidator();
        var options = new KeyCipherOptions
        {
            EncryptionKey = "not-valid-base64!!!",
            CurrentVersion = 1,
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
    }
}
