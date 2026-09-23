using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Maxkeys.Application.Persistence;

namespace Maxkeys.Application.Catalog;

/// <summary>
/// Binds the <c>Storage</c> configuration section (design section 10; ADR-12).
/// <see cref="R2PublicBaseUrl"/> is the public base URL used to resolve a
/// <c>Product.ImageKey</c> into a fully-qualified image URL. No S3 SDK, no S3
/// credentials — this is a URL builder only, per ADR-12.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string R2PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>ImageKit delivery base URL, for example <c>https://ik.imagekit.io/account</c>.</summary>
    public string ImageKitUrlEndpoint { get; set; } = string.Empty;

    /// <summary>ImageKit public upload key. Safe to return only from an admin-authorized endpoint.</summary>
    public string ImageKitPublicKey { get; set; } = string.Empty;

    /// <summary>ImageKit private signing key. Set only through backend secret configuration.</summary>
    public string ImageKitPrivateKey { get; set; } = string.Empty;

    /// <summary>Storage keys copied to ImageKit. Keys absent from this list continue resolving through R2.</summary>
    public string[] ImageKitMigratedKeys { get; set; } = [];

    /// <summary>Canonical folders permitted for browser-direct ImageKit uploads.</summary>
    public string[] ImageKitAllowedFolders { get; set; } = ["products/uploads", "carousel/uploads"];

    /// <summary>Maximum browser-direct image upload size in bytes.</summary>
    public long ImageKitMaxUploadBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Set only after the ImageKit server-side upload policy has been configured
    /// to permit image MIME types and files no larger than <see cref="ImageKitMaxUploadBytes"/>.
    /// ImageKit V1 upload signatures do not bind either constraint.
    /// </summary>
    public bool ImageKitUploadPolicyVerified { get; set; }
}

/// <summary>
/// Composes a public image URL from <see cref="StorageOptions.R2PublicBaseUrl"/>
/// and a stored R2 object key (catalog spec "Image URL Resolution"; ADR-12).
/// Concrete class, no interface — the URL join has exactly one implementation.
/// </summary>
public sealed class ImageUrlBuilder
{
    private readonly string _r2BaseUrl;
    private readonly string _imageKitUrlEndpoint;
    private readonly HashSet<string> _imageKitMigratedKeys;
    private readonly IAppDbContext? _db;

    public ImageUrlBuilder(IOptions<StorageOptions> options, IAppDbContext? db = null)
    {
        _r2BaseUrl = options.Value.R2PublicBaseUrl.TrimEnd('/');
        _imageKitUrlEndpoint = options.Value.ImageKitUrlEndpoint.TrimEnd('/');
        _imageKitMigratedKeys = new HashSet<string>(options.Value.ImageKitMigratedKeys, StringComparer.Ordinal);
        _db = db;
    }

    /// <summary>Returns the empty string when <paramref name="imageKey"/> is null or blank.</summary>
    public string Build(string? imageKey)
    {
        if (string.IsNullOrWhiteSpace(imageKey))
        {
            return string.Empty;
        }

        if (!ImageKeyPolicy.IsSafeRelativeKey(imageKey))
        {
            return string.Empty;
        }

        var isRegisteredImageKitAsset = _db?.ImageKitAssets.Any(asset => asset.FilePath == imageKey) == true;
        var baseUrl = (isRegisteredImageKitAsset || _imageKitMigratedKeys.Contains(imageKey)) && !string.IsNullOrWhiteSpace(_imageKitUrlEndpoint)
            ? _imageKitUrlEndpoint
            : _r2BaseUrl;

        var escapedKey = string.Join('/', imageKey.Split('/').Select(Uri.EscapeDataString));
        return $"{baseUrl}/{escapedKey}";
    }
}

/// <summary>Validates opaque image storage keys before persistence and URL resolution.</summary>
public static class ImageKeyPolicy
{
    public static bool IsSafeRelativeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.StartsWith('\\') ||
            key.Contains('\\') || key.Contains('?') || key.Contains('#') || Uri.IsWellFormedUriString(key, UriKind.Absolute))
        {
            return false;
        }

        var segments = key.Split('/');
        return segments.All(segment => segment.Length > 0 && segment is not "." and not ".." && !segment.Any(char.IsControl));
    }

    public static bool IsImageKitUploadKey(string key) =>
        key.StartsWith("products/", StringComparison.Ordinal) ||
        key.StartsWith("carousel/", StringComparison.Ordinal);

    /// <summary>
    /// Canonicalizes the single leading slash returned by ImageKit for permitted upload paths.
    /// Unsafe paths stay unchanged so normal validation rejects them.
    /// </summary>
    public static string? NormalizeImageKitUploadPath(string? key)
    {
        if (key is null || !key.StartsWith('/') || key.StartsWith("//", StringComparison.Ordinal))
        {
            return key;
        }

        var canonicalKey = key[1..];
        return IsSafeRelativeKey(canonicalKey) && IsImageKitUploadKey(canonicalKey)
            ? canonicalKey
            : key;
    }

    public static bool IsOptionalKeyValid(string? key) => string.IsNullOrWhiteSpace(key) || IsSafeRelativeKey(key);
}

/// <summary>Fail-closed gate for browser-direct ImageKit uploads.</summary>
public static class ImageKitUploadPolicy
{
    public static bool CanIssueClientUploadCredentials(StorageOptions options) =>
        options.ImageKitUploadPolicyVerified &&
        !string.IsNullOrWhiteSpace(options.ImageKitPrivateKey) &&
        !string.IsNullOrWhiteSpace(options.ImageKitPublicKey);
}

/// <summary>Registers <see cref="StorageOptions"/> and <see cref="ImageUrlBuilder"/>.</summary>
public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddStorageUrlBuilder(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName));

        services.AddScoped<ImageUrlBuilder>();

        return services;
    }
}
