using System.Net;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Fixtures;
using Maxkeys.Application.Catalog;
using Maxkeys.Domain.Catalog;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Catalog;

[Collection(ApiCollection.Name)]
public sealed class CatalogEndpointsTests
{
    private readonly ApiTestFixture _factory;

    public CatalogEndpointsTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Listing_returns_only_active_products()
    {
        var platform = UniquePlatform();
        await Seed(async db =>
        {
            var active = new Product($"active-{Guid.NewGuid():N}", "Active Product", platform);
            var inactive = new Product($"inactive-{Guid.NewGuid():N}", "Inactive Product", platform, isActive: false);
            db.Products.AddRange(active, inactive);
            db.ProductVariants.Add(new ProductVariant(active.Id, 100m, "ARS"));
            db.ProductVariants.Add(new ProductVariant(inactive.Id, 50m, "ARS"));
            await db.SaveChangesAsync();
        });

        var client = _factory.CreateClient();
        var products = await client.GetFromJsonAsync<List<ProductSummary>>($"/catalog/products?platform={platform}");

        var summary = Assert.Single(products!);
        Assert.Equal("Active Product", summary.Name);
    }

    [Fact]
    public async Task Platform_filter_narrows_results()
    {
        var platformA = UniquePlatform();
        var platformB = UniquePlatform();
        await Seed(async db =>
        {
            var a = new Product($"a-{Guid.NewGuid():N}", "Product A", platformA);
            var b = new Product($"b-{Guid.NewGuid():N}", "Product B", platformB);
            db.Products.AddRange(a, b);
            db.ProductVariants.Add(new ProductVariant(a.Id, 100m, "ARS"));
            db.ProductVariants.Add(new ProductVariant(b.Id, 100m, "ARS"));
            await db.SaveChangesAsync();
        });

        var client = _factory.CreateClient();
        var products = await client.GetFromJsonAsync<List<ProductSummary>>($"/catalog/products?platform={platformA}");

        Assert.Contains(products!, p => p.Platform == platformA);
        Assert.DoesNotContain(products!, p => p.Platform == platformB);
    }

    [Fact]
    public async Task Search_finds_matching_product_name()
    {
        var platform = UniquePlatform();
        var token = $"FcPoints{Guid.NewGuid():N}";
        await Seed(async db =>
        {
            var product = new Product($"p-{Guid.NewGuid():N}", $"{token} Card", platform);
            db.Products.Add(product);
            db.ProductVariants.Add(new ProductVariant(product.Id, 100m, "ARS"));
            await db.SaveChangesAsync();
        });

        var client = _factory.CreateClient();
        var products = await client.GetFromJsonAsync<List<ProductSummary>>($"/catalog/products?q={token}");

        Assert.Contains(products!, p => p.Platform == platform);
    }

    [Fact]
    public async Task Unknown_slug_returns_404_problem_details()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/catalog/products/does-not-exist-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static string UniquePlatform() => $"platform-{Guid.NewGuid():N}";

    private async Task Seed(Func<AppDbContext, Task> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await seed(db);
    }
}
