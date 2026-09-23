using Microsoft.Extensions.Options;
using Maxkeys.Application.Catalog;

namespace Maxkeys.Application.Tests.Catalog;

public sealed class ImageUrlBuilderTests
{
    [Fact]
    public void Build_uses_ImageKit_for_migrated_key()
    {
        var sut = new ImageUrlBuilder(Options.Create(new StorageOptions
        {
            R2PublicBaseUrl = "https://r2.example",
            ImageKitUrlEndpoint = "https://ik.imagekit.io/account",
            ImageKitMigratedKeys = ["products/elden-ring.png"],
        }));

        var result = sut.Build("products/elden-ring.png");

        Assert.Equal("https://ik.imagekit.io/account/products/elden-ring.png", result);
    }

    [Fact]
    public void Build_uses_R2_for_key_not_yet_migrated()
    {
        var sut = new ImageUrlBuilder(Options.Create(new StorageOptions
        {
            R2PublicBaseUrl = "https://r2.example",
            ImageKitUrlEndpoint = "https://ik.imagekit.io/account",
            ImageKitMigratedKeys = ["products/elden-ring.png"],
        }));

        var result = sut.Build("products/hades.png");

        Assert.Equal("https://r2.example/products/hades.png", result);
    }

    [Fact]
    public void Build_uses_R2_for_unregistered_upload_shaped_key()
    {
        var sut = new ImageUrlBuilder(Options.Create(new StorageOptions
        {
            R2PublicBaseUrl = "https://r2.example",
            ImageKitUrlEndpoint = "https://ik.imagekit.io/account",
        }));

        var result = sut.Build("products/uploads/new cover.png");

        Assert.Equal("https://r2.example/products/uploads/new%20cover.png", result);
    }

    [Fact]
    public void ImageKit_upload_auth_requires_explicitly_verified_server_policy()
    {
        var options = new StorageOptions
        {
            ImageKitPrivateKey = "private",
            ImageKitPublicKey = "public",
        };

        Assert.False(ImageKitUploadPolicy.CanIssueClientUploadCredentials(options));

        options.ImageKitUploadPolicyVerified = true;

        Assert.True(ImageKitUploadPolicy.CanIssueClientUploadCredentials(options));
    }

    [Theory]
    [InlineData("/products/image.png")]
    [InlineData("products/../private.png")]
    [InlineData("https://attacker.example/image.png")]
    [InlineData("products/image.png?width=1")]
    [InlineData("products/image.png#fragment")]
    public void Build_returns_empty_for_unsafe_key(string key)
    {
        var sut = new ImageUrlBuilder(Options.Create(new StorageOptions { R2PublicBaseUrl = "https://r2.example" }));

        Assert.Equal(string.Empty, sut.Build(key));
    }
}
