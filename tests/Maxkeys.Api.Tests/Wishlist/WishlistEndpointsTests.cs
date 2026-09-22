using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Wishlist;
using Maxkeys.Domain.Catalog;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Wishlist;

[Collection(Hs256ApiCollection.Name)]
public sealed class WishlistEndpointsTests
{
    private readonly Hs256ApiTestFixture _factory;

    public WishlistEndpointsTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_without_auth_returns_401()
    {
        var response = await _factory.CreateClient().GetAsync("/me/wishlist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_adds_an_active_product_and_get_returns_it()
    {
        var userId = Guid.NewGuid();
        var productId = await SeedActiveProductAsync();

        var postResponse = await UserClient(userId).PostAsJsonAsync("/me/wishlist", new { ProductId = productId });
        Assert.Equal(HttpStatusCode.NoContent, postResponse.StatusCode);

        var getResponse = await UserClient(userId).GetAsync("/me/wishlist");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var items = await getResponse.Content.ReadFromJsonAsync<List<WishlistItemSummary>>();
        Assert.Contains(items!, item => item.ProductId == productId);
    }

    [Fact]
    public async Task Post_for_unknown_product_returns_404()
    {
        var response = await UserClient(Guid.NewGuid()).PostAsJsonAsync("/me/wishlist", new { ProductId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_item_and_is_idempotent()
    {
        var userId = Guid.NewGuid();
        var productId = await SeedActiveProductAsync();
        await UserClient(userId).PostAsJsonAsync("/me/wishlist", new { ProductId = productId });

        var firstDelete = await UserClient(userId).DeleteAsync($"/me/wishlist/{productId}");
        var secondDelete = await UserClient(userId).DeleteAsync($"/me/wishlist/{productId}");

        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondDelete.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.WishlistItems.AnyAsync(w => w.UserId == userId && w.ProductId == productId));
    }

    private async Task<Guid> SeedActiveProductAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product($"wishlist-{Guid.NewGuid():N}", "Wishlist Test Product", "test-platform");
        db.Products.Add(product);
        db.ProductVariants.Add(new ProductVariant(product.Id, 10m, "ARS"));
        await db.SaveChangesAsync();
        return product.Id;
    }

    private HttpClient UserClient(Guid userId)
    {
        var token = TestTokens.CreateHs256(
            userId.ToString(), Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
