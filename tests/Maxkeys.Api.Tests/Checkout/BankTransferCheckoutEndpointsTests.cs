using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maxkeys.Api.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Checkout;

/// <summary>
/// Bank-transfer checkout over HTTP: <c>GET /checkout/bank-transfer</c>, the
/// <c>paymentMethod</c>/<c>phone</c> members of <c>POST /checkout/orders</c>, and the
/// <c>paymentMethod</c> field of the status/reconcile responses. The test host runs in
/// Development, so <c>appsettings.Development.json</c>'s sample account enables the feature;
/// <see cref="CreateDisabledFactory"/> blanks it out.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BankTransferCheckoutEndpointsTests
{
    private readonly ApiTestFixture _factory;

    public BankTransferCheckoutEndpointsTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Bank_transfer_details_are_public_when_configured()
    {
        var response = await _factory.CreateClient().GetAsync("/checkout/bank-transfer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("chekeys.mp", body.RootElement.GetProperty("alias").GetString());
        Assert.Equal("Chekeys Dev", body.RootElement.GetProperty("accountHolder").GetString());
        Assert.True(body.RootElement.TryGetProperty("cbu", out _));
    }

    [Fact]
    public async Task Bank_transfer_details_are_404_problem_details_when_disabled()
    {
        await using var disabled = CreateDisabledFactory();
        var response = await disabled.CreateClient().GetAsync("/checkout/bank-transfer");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Bank_transfer_checkout_returns_201_without_init_point_and_awaits_validation()
    {
        var variantId = await SeedActiveVariant();
        var buyerEmail = $"transfer-{Guid.NewGuid():N}@example.com";

        var response = await _factory.CreateClient().PostAsJsonAsync("/checkout/orders", new
        {
            email = buyerEmail,
            items = new[] { new { variantId, quantity = 2 } },
            paymentMethod = "bank-transfer",
            phone = "+54 (11) 1234-5678",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var orderId = body.RootElement.GetProperty("orderId").GetGuid();
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("initPoint").ValueKind);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingTransferValidation, order.Status);
        Assert.Equal(PaymentMethod.BankTransfer, order.PaymentMethod);
        Assert.Equal("+541112345678", order.BuyerPhone);
        Assert.Equal(200m, order.TotalAmount);
        Assert.Contains(
            await new OutboxEventReader(db).ListByOrderIdAsync(orderId),
            evt => evt.Type == OutboxEventTypes.TransferSubmitted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12-34")]
    [InlineData("phone me")]
    public async Task Bank_transfer_with_invalid_phone_is_422_with_phone_error(string? phone)
    {
        var variantId = await SeedActiveVariant();

        var response = await _factory.CreateClient().PostAsJsonAsync("/checkout/orders", new
        {
            email = "buyer@example.com",
            items = new[] { new { variantId, quantity = 1 } },
            paymentMethod = "bank-transfer",
            phone,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var phoneErrors = body.RootElement.GetProperty("errors").GetProperty("phone");
        Assert.Equal(JsonValueKind.Array, phoneErrors.ValueKind);
        Assert.NotEmpty(phoneErrors.EnumerateArray());
    }

    [Fact]
    public async Task Unknown_payment_method_is_422_with_payment_method_error()
    {
        var variantId = await SeedActiveVariant();

        var response = await _factory.CreateClient().PostAsJsonAsync("/checkout/orders", new
        {
            email = "buyer@example.com",
            items = new[] { new { variantId, quantity = 1 } },
            paymentMethod = "cash",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("paymentMethod", out _));
    }

    [Fact]
    public async Task Bank_transfer_while_disabled_is_503_bank_transfer_unavailable()
    {
        var variantId = await SeedActiveVariant();
        var buyerEmail = $"transfer-{Guid.NewGuid():N}@example.com";

        await using var disabled = CreateDisabledFactory();
        var response = await disabled.CreateClient().PostAsJsonAsync("/checkout/orders", new
        {
            email = buyerEmail,
            items = new[] { new { variantId, quantity = 1 } },
            paymentMethod = "bank-transfer",
            phone = "1112345678",
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Bank transfer unavailable", body.RootElement.GetProperty("title").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Orders.AnyAsync(o => o.BuyerEmail == buyerEmail));
    }

    [Fact]
    public async Task Explicit_mercadopago_payment_method_keeps_the_existing_gateway_path()
    {
        var variantId = await SeedActiveVariant();

        // No Payments:AccessToken in the test host, so reaching the gateway means the 503 it always returns.
        var response = await _factory.CreateClient().PostAsJsonAsync("/checkout/orders", new
        {
            email = "buyer@example.com",
            items = new[] { new { variantId, quantity = 1 } },
            paymentMethod = "mercadopago",
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Service Unavailable", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Status_and_reconcile_report_payment_method_without_touching_the_gateway()
    {
        var transferOrderId = await SeedOrderAsync(PaymentMethod.BankTransfer);
        var mpOrderId = await SeedOrderAsync(PaymentMethod.MercadoPago);
        var client = _factory.CreateClient();

        using (var transferStatus = JsonDocument.Parse(await client.GetStringAsync($"/checkout/orders/{transferOrderId}/status")))
        {
            Assert.Equal("AwaitingTransferValidation", transferStatus.RootElement.GetProperty("status").GetString());
            Assert.Equal("bank-transfer", transferStatus.RootElement.GetProperty("paymentMethod").GetString());
        }

        using (var mpStatus = JsonDocument.Parse(await client.GetStringAsync($"/checkout/orders/{mpOrderId}/status")))
        {
            Assert.Equal("Pending", mpStatus.RootElement.GetProperty("status").GetString());
            Assert.Equal("mercadopago", mpStatus.RootElement.GetProperty("paymentMethod").GetString());
        }

        // Reconcile would hit the (unconfigured → 503) gateway for an MP order with a preference;
        // a bank-transfer order must come back 200 with its status unchanged instead.
        var reconcile = await client.PostAsync($"/checkout/orders/{transferOrderId}/reconcile", content: null);

        Assert.Equal(HttpStatusCode.OK, reconcile.StatusCode);
        using var reconciled = JsonDocument.Parse(await reconcile.Content.ReadAsStringAsync());
        Assert.Equal("AwaitingTransferValidation", reconciled.RootElement.GetProperty("status").GetString());
        Assert.Equal("bank-transfer", reconciled.RootElement.GetProperty("paymentMethod").GetString());
    }

    private WebApplicationFactory<Program> CreateDisabledFactory() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configBuilder) =>
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BankTransfer:Alias"] = string.Empty,
                ["BankTransfer:AccountHolder"] = string.Empty,
            })));

    private async Task<Guid> SeedOrderAsync(PaymentMethod paymentMethod)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)],
            DateTimeOffset.UtcNow,
            paymentMethod,
            "1112345678");
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return order.Id;
    }

    private async Task<Guid> SeedActiveVariant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product($"p-{Guid.NewGuid():N}", "Bank Transfer Test Product", $"platform-{Guid.NewGuid():N}");
        db.Products.Add(product);
        var variant = new ProductVariant(product.Id, 100m, "ARS");
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        return variant.Id;
    }
}
