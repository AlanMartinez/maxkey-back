using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Catalog;
using Maxkeys.Domain.Catalog;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>
/// Covers the admin-catalog spec: Admin Product Listing Including Inactive,
/// Admin Product Content Update, Admin Product Activation Toggle, Admin
/// Product Soft-Delete, Admin Variant Creation, Admin Variant Activation
/// Toggle, Admin Catalog Authorization.
/// </summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AdminCatalogEndpointsTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private readonly Hs256ApiTestFixture _factory;

    public AdminCatalogEndpointsTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_to_list_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/admin/catalog/products");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden_from_list()
    {
        var response = await AdminClient(NonAdminSub).GetAsync("/admin/catalog/products");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_listing_includes_inactive_products()
    {
        var platform = UniquePlatform();
        var inactiveSlug = $"inactive-{Guid.NewGuid():N}";
        await Seed(async db =>
        {
            db.Products.Add(new Product(inactiveSlug, "Inactive Product", platform, isActive: false));
            await db.SaveChangesAsync();
        });

        var response = await AdminClient().GetAsync("/admin/catalog/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var products = await response.Content.ReadFromJsonAsync<List<AdminProduct>>();
        Assert.Contains(products!, p => p.Slug == inactiveSlug && !p.IsActive);
    }

    [Fact]
    public async Task Anonymous_request_to_create_is_rejected()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/admin/catalog/products", new
        {
            slug = $"p-{Guid.NewGuid():N}",
            name = "Name",
            platform = "PSN",
            isActive = true,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_a_product_with_gallery_and_activation_fields()
    {
        var slug = $"p-{Guid.NewGuid():N}";

        var response = await AdminClient().PostAsJsonAsync("/admin/catalog/products", new
        {
            slug,
            name = "New Product",
            platform = UniquePlatform(),
            description = "A brand new product",
            imageKey = "products/new.png",
            detailImageKey = "products/new-detail.png",
            isActive = true,
            imageKeys = new[] { "products/gallery/1.png", "products/gallery/2.png" },
            activationGuide = "**Step 1.** Open the launcher and redeem the key.",
            activationType = "Clave de activación",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AdminProduct>();
        Assert.Equal(slug, created!.Slug);
        Assert.Equal("New Product", created.Name);
        Assert.Equal(["products/gallery/1.png", "products/gallery/2.png"], created.ImageKeys);
        Assert.Equal("**Step 1.** Open the launcher and redeem the key.", created.ActivationGuide);
        Assert.Equal("Clave de activación", created.ActivationType);

        var listResponse = await AdminClient().GetAsync("/admin/catalog/products");
        var products = await listResponse.Content.ReadFromJsonAsync<List<AdminProduct>>();
        Assert.Contains(products!, p => p.Slug == slug);
    }

    [Fact]
    public async Task Creating_a_product_with_a_duplicate_slug_returns_409()
    {
        var slug = $"p-{Guid.NewGuid():N}";
        await Seed(async db =>
        {
            db.Products.Add(new Product(slug, "Existing Product", UniquePlatform()));
            await db.SaveChangesAsync();
        });

        var response = await AdminClient().PostAsJsonAsync("/admin/catalog/products", new
        {
            slug,
            name = "Duplicate Slug Product",
            platform = UniquePlatform(),
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_update_product_description_and_image_key()
    {
        var productId = await SeedProductAsync();

        var response = await AdminClient().PutAsJsonAsync($"/admin/catalog/products/{productId}", new
        {
            name = "Updated Name",
            platform = "PSN",
            description = "Updated description",
            imageKey = "products/updated.png",
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AdminProduct>();
        Assert.Equal("Updated Name", updated!.Name);
        Assert.Equal("Updated description", updated.Description);
    }

    [Fact]
    public async Task Empty_required_field_on_product_update_returns_422()
    {
        var platform = UniquePlatform();
        Guid productId = default;
        await Seed(async db =>
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Original Name", platform);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
        });

        var response = await AdminClient().PutAsJsonAsync($"/admin/catalog/products/{productId}", new
        {
            name = "",
            platform = "PSN",
            description = (string?)null,
            imageKey = (string?)null,
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var listResponse = await AdminClient().GetAsync("/admin/catalog/products");
        var products = await listResponse.Content.ReadFromJsonAsync<List<AdminProduct>>();
        var unchanged = Assert.Single(products!, p => p.Id == productId);
        Assert.Equal("Original Name", unchanged.Name);
        Assert.Equal(platform, unchanged.Platform);
    }

    /// <summary>Admin Product Activation Toggle — "Product deactivated" spec scenario.</summary>
    [Fact]
    public async Task Deactivating_a_product_hides_it_from_the_public_catalog_listing()
    {
        var platform = UniquePlatform();
        var slug = $"p-{Guid.NewGuid():N}";
        Guid productId = default;
        await Seed(async db =>
        {
            var product = new Product(slug, "Visible Product", platform);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
        });

        var publicClient = _factory.CreateClient();
        var before = await publicClient.GetFromJsonAsync<List<ProductSummary>>($"/catalog/products?platform={platform}");
        Assert.Contains(before!, p => p.Slug == slug);

        var putResponse = await AdminClient().PutAsJsonAsync($"/admin/catalog/products/{productId}", new
        {
            name = "Visible Product",
            platform,
            description = (string?)null,
            imageKey = (string?)null,
            isActive = false,
        });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var after = await publicClient.GetFromJsonAsync<List<ProductSummary>>($"/catalog/products?platform={platform}");
        Assert.DoesNotContain(after!, p => p.Slug == slug);
    }

    /// <summary>Admin Variant Activation Toggle — "Variant deactivated" spec scenario.</summary>
    [Fact]
    public async Task Deactivating_a_variant_hides_it_from_the_public_product_detail()
    {
        var platform = UniquePlatform();
        var slug = $"p-{Guid.NewGuid():N}";
        Guid variantId = default;
        await Seed(async db =>
        {
            var product = new Product(slug, "Product With Variant", platform);
            db.Products.Add(product);
            var variant = new ProductVariant(product.Id, 100m, "ARS", region: "AR", edition: "Standard");
            db.ProductVariants.Add(variant);
            await db.SaveChangesAsync();
            variantId = variant.Id;
        });

        var publicClient = _factory.CreateClient();
        var before = await publicClient.GetFromJsonAsync<ProductDetail>($"/catalog/products/{slug}");
        Assert.Contains(before!.Variants, v => v.Id == variantId);

        var putResponse = await AdminClient().PutAsJsonAsync($"/admin/catalog/variants/{variantId}", new
        {
            price = 100m,
            discountPercentage = (decimal?)null,
            currency = "ARS",
            region = "AR",
            edition = "Standard",
            sortOrder = 0,
            isActive = false,
            isRecommended = false,
        });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var after = await publicClient.GetFromJsonAsync<ProductDetail>($"/catalog/products/{slug}");
        Assert.DoesNotContain(after!.Variants, v => v.Id == variantId);
    }

    [Fact]
    public async Task Unknown_product_id_returns_404()
    {
        var response = await AdminClient().PutAsJsonAsync($"/admin/catalog/products/{Guid.NewGuid()}", new
        {
            name = "Name",
            platform = "PSN",
            description = (string?)null,
            imageKey = (string?)null,
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_deactivate_a_variant()
    {
        var variantId = await SeedVariantAsync();

        var response = await AdminClient().PutAsJsonAsync($"/admin/catalog/variants/{variantId}", new
        {
            price = 150m,
            discountPercentage = (decimal?)null,
            currency = "ARS",
            region = "AR",
            edition = "Standard",
            sortOrder = 0,
            isActive = false,
            isRecommended = false,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AdminVariant>();
        Assert.False(updated!.IsActive);
    }

    [Fact]
    public async Task Admin_can_mark_a_variant_as_recommended()
    {
        var variantId = await SeedVariantAsync();

        var response = await AdminClient().PutAsJsonAsync($"/admin/catalog/variants/{variantId}", new
        {
            price = 100m,
            discountPercentage = (decimal?)null,
            currency = "ARS",
            region = "AR",
            edition = "Standard",
            sortOrder = 0,
            isActive = true,
            isRecommended = true,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AdminVariant>();
        Assert.True(updated!.IsRecommended);
    }

    [Fact]
    public async Task Invalid_variant_price_returns_422()
    {
        var variantId = await SeedVariantAsync();

        var response = await AdminClient().PutAsJsonAsync($"/admin/catalog/variants/{variantId}", new
        {
            price = 0m,
            discountPercentage = (decimal?)null,
            currency = "ARS",
            region = (string?)null,
            edition = (string?)null,
            sortOrder = 0,
            isActive = true,
            isRecommended = false,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Out_of_range_discount_percentage_returns_422()
    {
        var variantId = await SeedVariantAsync();

        var response = await AdminClient().PutAsJsonAsync($"/admin/catalog/variants/{variantId}", new
        {
            price = 100m,
            discountPercentage = (decimal?)100m,
            currency = "ARS",
            region = (string?)null,
            edition = (string?)null,
            sortOrder = 0,
            isActive = true,
            isRecommended = false,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>Admin catalog UX: hard-delete removes the variant row outright, unlike the isActive toggle.</summary>
    [Fact]
    public async Task Admin_can_hard_delete_a_variant()
    {
        var variantId = await SeedVariantAsync();

        var response = await AdminClient().DeleteAsync($"/admin/catalog/variants/{variantId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var listResponse = await AdminClient().GetAsync("/admin/catalog/products");
        var products = await listResponse.Content.ReadFromJsonAsync<List<AdminProduct>>();
        Assert.DoesNotContain(products!.SelectMany(p => p.Variants), v => v.Id == variantId);
    }

    [Fact]
    public async Task Deleting_unknown_variant_returns_404()
    {
        var response = await AdminClient().DeleteAsync($"/admin/catalog/variants/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Admin Product Soft-Delete — "Product soft-deleted": the row stays, only isActive flips.</summary>
    [Fact]
    public async Task Admin_can_soft_delete_a_product()
    {
        var productId = await SeedProductAsync();

        var response = await AdminClient().DeleteAsync($"/admin/catalog/products/{productId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deleted = await response.Content.ReadFromJsonAsync<AdminProduct>();
        Assert.Equal(productId, deleted!.Id);
        Assert.False(deleted.IsActive);
        Assert.Equal("Original Name", deleted.Name);

        var listResponse = await AdminClient().GetAsync("/admin/catalog/products");
        var products = await listResponse.Content.ReadFromJsonAsync<List<AdminProduct>>();
        Assert.Contains(products!, p => p.Id == productId && !p.IsActive);
    }

    /// <summary>Admin Product Soft-Delete — "Product not found".</summary>
    [Fact]
    public async Task Deleting_an_unknown_product_returns_404()
    {
        var response = await AdminClient().DeleteAsync($"/admin/catalog/products/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_to_delete_product_is_rejected()
    {
        var response = await _factory.CreateClient().DeleteAsync($"/admin/catalog/products/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden_from_deleting_product()
    {
        var response = await AdminClient(NonAdminSub).DeleteAsync($"/admin/catalog/products/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Admin Variant Creation — "Variant created".</summary>
    [Fact]
    public async Task Admin_can_create_a_variant_for_an_existing_product()
    {
        var productId = await SeedProductAsync();

        var response = await AdminClient().PostAsJsonAsync($"/admin/catalog/products/{productId}/variants", new
        {
            region = "AR",
            edition = "Standard",
            price = 100m,
            discountPercentage = (decimal?)null,
            currency = "ARS",
            sortOrder = 0,
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AdminVariant>();
        Assert.Equal(100m, created!.Price);
        Assert.Equal("AR", created.Region);
        Assert.Equal($"/admin/catalog/variants/{created.Id}", response.Headers.Location?.ToString());

        var listResponse = await AdminClient().GetAsync("/admin/catalog/products");
        var products = await listResponse.Content.ReadFromJsonAsync<List<AdminProduct>>();
        var product = Assert.Single(products!, p => p.Id == productId);
        Assert.Contains(product.Variants, v => v.Id == created.Id);
    }

    /// <summary>Admin Variant Creation — "Invalid invariant rejected".</summary>
    [Fact]
    public async Task Creating_a_variant_with_a_non_positive_price_returns_422()
    {
        var productId = await SeedProductAsync();

        var response = await AdminClient().PostAsJsonAsync($"/admin/catalog/products/{productId}/variants", new
        {
            region = "AR",
            edition = "Standard",
            price = 0m,
            discountPercentage = (decimal?)null,
            currency = "ARS",
            sortOrder = 0,
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Admin Variant Creation — "Parent product not found".</summary>
    [Fact]
    public async Task Creating_a_variant_for_an_unknown_product_returns_404()
    {
        var response = await AdminClient().PostAsJsonAsync($"/admin/catalog/products/{Guid.NewGuid()}/variants", new
        {
            region = "AR",
            edition = "Standard",
            price = 100m,
            discountPercentage = (decimal?)null,
            currency = "ARS",
            sortOrder = 0,
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_to_create_variant_is_rejected()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync($"/admin/catalog/products/{Guid.NewGuid()}/variants", new
        {
            region = "AR",
            edition = "Standard",
            price = 100m,
            discountPercentage = (decimal?)null,
            currency = "ARS",
            sortOrder = 0,
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden_from_creating_variant()
    {
        var response = await AdminClient(NonAdminSub).PostAsJsonAsync($"/admin/catalog/products/{Guid.NewGuid()}/variants", new
        {
            region = "AR",
            edition = "Standard",
            price = 100m,
            discountPercentage = (decimal?)null,
            currency = "ARS",
            sortOrder = 0,
            isActive = true,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Non_whitelisted_currency_returns_422()
    {
        var variantId = await SeedVariantAsync();

        var response = await AdminClient().PutAsJsonAsync($"/admin/catalog/variants/{variantId}", new
        {
            price = 100m,
            discountPercentage = (decimal?)null,
            currency = "EUR",
            region = (string?)null,
            edition = (string?)null,
            sortOrder = 0,
            isActive = true,
            isRecommended = false,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static string UniquePlatform() => $"platform-{Guid.NewGuid():N}";

    private async Task<Guid> SeedProductAsync()
    {
        Guid id = default;
        await Seed(async db =>
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Original Name", UniquePlatform());
            db.Products.Add(product);
            await db.SaveChangesAsync();
            id = product.Id;
        });
        return id;
    }

    private async Task<Guid> SeedVariantAsync()
    {
        Guid id = default;
        await Seed(async db =>
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Product", UniquePlatform());
            db.Products.Add(product);
            var variant = new ProductVariant(product.Id, 100m, "ARS", region: "AR", edition: "Standard");
            db.ProductVariants.Add(variant);
            await db.SaveChangesAsync();
            id = variant.Id;
        });
        return id;
    }

    private async Task Seed(Func<AppDbContext, Task> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await seed(db);
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
