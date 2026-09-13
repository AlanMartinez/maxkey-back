using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Security;

/// <summary>
/// Binds the <c>Keys</c> configuration section (design section 4.2/8; ADR-05).
/// <see cref="EncryptionKey"/> is the base64 encoding of the AES-256 key used by
/// <see cref="KeyCipher"/> (env var <c>Keys__EncryptionKey</c>) and MUST decode to
/// exactly 32 bytes. <see cref="CurrentVersion"/> is the key version used for new
/// encryptions; a future rotation adds more versions without a schema change.
/// </summary>
public sealed class KeyCipherOptions
{
    public const string SectionName = "Keys";

    public string EncryptionKey { get; set; } = string.Empty;

    public short CurrentVersion { get; set; } = 1;
}

/// <summary>
/// Fails DI options validation (fail-fast at startup once <c>ValidateOnStart</c>
/// is wired in the API slice) when <see cref="KeyCipherOptions.EncryptionKey"/> is
/// not valid base64, does not decode to 32 bytes, or <see cref="KeyCipherOptions.CurrentVersion"/>
/// is out of range. Never includes the key value itself in a failure message.
/// </summary>
public sealed class KeyCipherOptionsValidator : IValidateOptions<KeyCipherOptions>
{
    public ValidateOptionsResult Validate(string? name, KeyCipherOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EncryptionKey))
        {
            return ValidateOptionsResult.Fail("Keys:EncryptionKey is required.");
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(options.EncryptionKey);
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail("Keys:EncryptionKey must be a valid base64 string.");
        }

        if (keyBytes.Length != 32)
        {
            return ValidateOptionsResult.Fail(
                $"Keys:EncryptionKey must decode to exactly 32 bytes, got {keyBytes.Length}.");
        }

        if (options.CurrentVersion < 1)
        {
            return ValidateOptionsResult.Fail("Keys:CurrentVersion must be >= 1.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>Registers <see cref="KeyCipherOptions"/>, its validator, and <see cref="KeyCipher"/>.</summary>
public static class KeyCipherServiceCollectionExtensions
{
    public static IServiceCollection AddKeyCipher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KeyCipherOptions>()
            .Bind(configuration.GetSection(KeyCipherOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<KeyCipherOptions>, KeyCipherOptionsValidator>();
        services.AddSingleton<KeyCipher>();

        return services;
    }
}
