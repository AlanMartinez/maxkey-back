using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>
/// Covers <c>GET /admin/orders/{id}</c> (admin-buyers spec: Order Detail,
/// Key Exposure in Buyer View — the response must never carry a key code).
/// </summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AdminOrderDetailEndpointTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";
    private const string PlaintextCode = "CODE-ONE";

    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly Hs256ApiTestFixture _factory;

    public AdminOrderDetailEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync($"/admin/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden()
    {
        var response = await AdminClient(NonAdminSub).GetAsync($"/admin/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_order_returns_404_problem()
    {
        var response = await AdminClient().GetAsync($"/admin/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Delivered_order_returns_200_with_detail_and_no_key_code()
    {
        var orderId = await SeedDeliveredOrderAsync();

        var response = await AdminClient().GetAsync($"/admin/orders/{orderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(raw);
        var root = body.RootElement;

        Assert.Equal(orderId, root.GetProperty("id").GetGuid());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("buyerEmail").GetString()));
        Assert.Equal("Delivered", root.GetProperty("status").GetString());

        var item0 = root.GetProperty("items")[0];
        Assert.Equal("Product", item0.GetProperty("productName").GetString());
        var key0 = item0.GetProperty("keys")[0];
        Assert.NotEqual(Guid.Empty, key0.GetProperty("keyId").GetGuid());
        Assert.Equal("Assigned", key0.GetProperty("status").GetString());

        Assert.DoesNotContain(PlaintextCode, raw);
        Assert.DoesNotContain("encryptedCode", raw, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> SeedDeliveredOrderAsync()
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(
            null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var item = order.Items.Single();
        var (blob, version) = cipher.Encrypt(PlaintextCode);
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
        db.Keys.Add(key); // client-generated Id — must be added explicitly (see AttachKeyToOrderItem)
        order.AttachKey(item.Id, key, now);
        order.MarkDelivered(now);
        await db.SaveChangesAsync();

        return order.Id;
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
