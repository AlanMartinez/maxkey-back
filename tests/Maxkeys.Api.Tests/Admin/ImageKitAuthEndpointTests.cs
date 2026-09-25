using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Admin;

[Collection(Hs256ApiCollection.Name)]
public sealed class ImageKitAuthEndpointTests
{
    private const string NonAdminSub = "33333333-3333-3333-3333-333333333333";
    private readonly Hs256ApiTestFixture _factory;

    public ImageKitAuthEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/admin/media/imagekit-auth");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_request_is_forbidden()
    {
        var response = await AuthorizedClient(NonAdminSub).GetAsync("/admin/media/imagekit-auth");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_request_receives_valid_ImageKit_signature()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var response = await AuthorizedClient(Hs256ApiTestFixture.AdminSub).GetAsync("/admin/media/imagekit-auth");
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("token").GetString();
        var signature = body.RootElement.GetProperty("signature").GetString();
        var expire = body.RootElement.GetProperty("expire").GetInt64();

        Assert.True(Guid.TryParse(token, out _));
        Assert.Equal("public_test_key", body.RootElement.GetProperty("publicKey").GetString());
        Assert.InRange(expire, before + 1, after + 3600);
        Assert.Equal(Sign(token!, expire), signature);
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cacheControl));
        Assert.Contains("no-store", cacheControl!);
    }

    [Fact]
    public async Task Admin_can_register_uploaded_product_asset_for_durable_ImageKit_delivery()
    {
        var filePath = $"products/{Guid.NewGuid():N}.png";

        var response = await AuthorizedClient(Hs256ApiTestFixture.AdminSub)
            .PostAsJsonAsync("/admin/media/imagekit-assets", new { filePath });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Admin_registration_canonicalizes_ImageKit_file_path_with_leading_slash()
    {
        var filePath = $"/carousel/{Guid.NewGuid():N}.png";

        var response = await AuthorizedClient(Hs256ApiTestFixture.AdminSub)
            .PostAsJsonAsync("/admin/media/imagekit-assets", new { filePath });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains(db.ImageKitAssets, asset => asset.FilePath == filePath[1..]);
        Assert.DoesNotContain(db.ImageKitAssets, asset => asset.FilePath == filePath);
    }

    [Theory]
    [InlineData("other/image.png")]
    [InlineData("products/../private.png")]
    public async Task Registration_rejects_key_outside_allowed_ImageKit_prefixes(string filePath)
    {
        var response = await AuthorizedClient(Hs256ApiTestFixture.AdminSub)
            .PostAsJsonAsync("/admin/media/imagekit-assets", new { filePath });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>activation-guides: images inserted into guide Markdown upload to guides/ via MarkdownEditor's autoUpload mode.</summary>
    [Fact]
    public async Task Admin_can_register_uploaded_guide_asset()
    {
        var filePath = $"guides/{Guid.NewGuid():N}.png";

        var response = await AuthorizedClient(Hs256ApiTestFixture.AdminSub)
            .PostAsJsonAsync("/admin/media/imagekit-assets", new { filePath });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private HttpClient AuthorizedClient(string sub)
    {
        var token = TestTokens.CreateHs256(sub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Sign(string token, long expire)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes("private_test_key"));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(token + expire))).ToLowerInvariant();
    }
}
