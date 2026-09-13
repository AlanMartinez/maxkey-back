using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
}

/// <summary>
/// Composes a public image URL from <see cref="StorageOptions.R2PublicBaseUrl"/>
/// and a stored R2 object key (catalog spec "Image URL Resolution"; ADR-12).
/// Concrete class, no interface — the URL join has exactly one implementation.
/// </summary>
public sealed class ImageUrlBuilder
{
    private readonly string _baseUrl;

    public ImageUrlBuilder(IOptions<StorageOptions> options)
    {
        _baseUrl = options.Value.R2PublicBaseUrl.TrimEnd('/');
    }

    /// <summary>Returns the empty string when <paramref name="imageKey"/> is null or blank.</summary>
    public string Build(string? imageKey)
    {
        if (string.IsNullOrWhiteSpace(imageKey))
        {
            return string.Empty;
        }

        return $"{_baseUrl}/{imageKey.TrimStart('/')}";
    }
}

/// <summary>Registers <see cref="StorageOptions"/> and <see cref="ImageUrlBuilder"/>.</summary>
public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddStorageUrlBuilder(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName));

        services.AddSingleton<ImageUrlBuilder>();

        return services;
    }
}
