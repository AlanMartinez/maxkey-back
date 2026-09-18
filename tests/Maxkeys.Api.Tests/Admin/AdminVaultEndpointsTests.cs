using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Fulfillment;
using Maxkeys.Domain.Catalog;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>Covers the vault spec: Bulk Key Load, Per-Product Vault Toggle, Vault Stock Listing, Vault Admin Authorization.</summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AdminVaultEndpointsTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private readonly Hs256ApiTestFixture _factory;

    public AdminVaultEndpointsTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_to_list_stock_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/admin/vault/products");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden_from_list_stock()
    {
        var response = await AdminClient(NonAdminSub).GetAsync("/admin/vault/products");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_list_vault_stock()
    {
        var (productId, variantId) = await SeedProductWithVariantAsync();

        var response = await AdminClient().GetAsync("/admin/vault/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var products = await response.Content.ReadFromJsonAsync<List<VaultProduct>>();
        var product = Assert.Single(products!, p => p.Id == productId);
        Assert.Single(product.Variants, v => v.Id == variantId);
    }

    [Fact]
    public async Task Admin_can_load_vault_keys_for_a_variant()
    {
        var (_, variantId) = await SeedProductWithVariantAsync();

        var response = await AdminClient().PostAsJsonAsync(
            $"/admin/vault/variants/{variantId}/keys", new { codes = new[] { "CODE-1", "CODE-2" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoadVaultKeysResult>();
        Assert.Equal(2, result!.AddedCount);
        Assert.Equal(2, result.AvailableCount);
    }

    [Fact]
    public async Task Loading_keys_for_an_unknown_variant_returns_404()
    {
        var response = await AdminClient().PostAsJsonAsync(
            $"/admin/vault/variants/{Guid.NewGuid()}/keys", new { codes = new[] { "CODE-1" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_to_list_variant_keys_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync($"/admin/vault/variants/{Guid.NewGuid()}/keys");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden_from_list_variant_keys()
    {
        var response = await AdminClient(NonAdminSub).GetAsync($"/admin/vault/variants/{Guid.NewGuid()}/keys");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_list_keys_for_a_variant_with_loaded_stock()
    {
        var (_, variantId) = await SeedProductWithVariantAsync();
        var loadResponse = await AdminClient().PostAsJsonAsync(
            $"/admin/vault/variants/{variantId}/keys", new { codes = new[] { "CODE-1", "CODE-2" } });
        Assert.Equal(HttpStatusCode.OK, loadResponse.StatusCode);

        var response = await AdminClient().GetAsync($"/admin/vault/variants/{variantId}/keys");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("encryptedcode", rawBody, StringComparison.OrdinalIgnoreCase);

        var keys = await response.Content.ReadFromJsonAsync<List<VaultKeySummary>>();
        Assert.Equal(2, keys!.Count);
        Assert.All(keys, k => Assert.Equal("Available", k.Status));
        Assert.All(keys, k => Assert.False(string.IsNullOrWhiteSpace(k.LoadedBy)));
        Assert.All(keys, k => Assert.Null(k.AssignedAt));
        Assert.All(keys, k => Assert.Null(k.OrderItemId));
    }

    [Fact]
    public async Task Listing_keys_for_a_variant_with_no_stock_returns_an_empty_list()
    {
        var (_, variantId) = await SeedProductWithVariantAsync();

        var response = await AdminClient().GetAsync($"/admin/vault/variants/{variantId}/keys");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var keys = await response.Content.ReadFromJsonAsync<List<VaultKeySummary>>();
        Assert.Empty(keys!);
    }

    [Fact]
    public async Task Listing_keys_for_an_unknown_variant_returns_404()
    {
        var response = await AdminClient().GetAsync($"/admin/vault/variants/{Guid.NewGuid()}/keys");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_toggle_a_products_vault()
    {
        var (productId, _) = await SeedProductWithVariantAsync();

        var response = await AdminClient().PutAsJsonAsync(
            $"/admin/vault/products/{productId}/toggle", new { enabled = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listResponse = await AdminClient().GetAsync("/admin/vault/products");
        var products = await listResponse.Content.ReadFromJsonAsync<List<VaultProduct>>();
        Assert.True(products!.Single(p => p.Id == productId).VaultEnabled);
    }

    [Fact]
    public async Task Toggling_an_unknown_product_returns_404()
    {
        var response = await AdminClient().PutAsJsonAsync(
            $"/admin/vault/products/{Guid.NewGuid()}/toggle", new { enabled = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid ProductId, Guid VariantId)> SeedProductWithVariantAsync()
    {
        Guid productId = default, variantId = default;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product($"p-{Guid.NewGuid():N}", "Product", $"platform-{Guid.NewGuid():N}");
        db.Products.Add(product);
        var variant = new ProductVariant(product.Id, 100m, "ARS", region: "AR", edition: "Standard");
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        productId = product.Id;
        variantId = variant.Id;
        return (productId, variantId);
    }

    private HttpClient AdminClient(string? sub = null)
    {
        var token = TestTokens.CreateHs256(
            sub ?? Hs256ApiTestFixture.AdminSub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
