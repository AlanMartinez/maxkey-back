using Maxkeys.Application.Buyers;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Buyers;

/// <summary>
/// Covers <see cref="ListBuyers"/> (admin-buyers spec: Buyer Listing Grouped
/// By Email, Key Exposure in Buyer View; design D4): orders for the same
/// buyer nest under one entry, unpaid orders are excluded, email search
/// narrows results, pagination bounds page size, and only the assigned-key
/// count is exposed per item — never a key code.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ListBuyersTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public ListBuyersTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Buyer_with_multiple_paid_orders_is_grouped_and_unpaid_orders_are_excluded()
    {
        var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
        await SeedPaidOrderAsync(buyerEmail);
        await SeedPaidOrderAsync(buyerEmail);
        await SeedUnpaidOrderAsync(buyerEmail);

        await using var context = _fixture.CreateContext();
        var result = await CreateSut(context).ExecuteAsync(email: null, page: 1, pageSize: 20);

        var buyer = Assert.Single(result.Items, b => b.Email == buyerEmail);
        Assert.Equal(2, buyer.OrderCount);
        Assert.Equal(2, buyer.Orders.Count);
    }

    [Fact]
    public async Task Email_search_narrows_results_to_the_matching_buyer()
    {
        var emailA = $"a-{Guid.NewGuid():N}@x.com";
        var emailB = $"b-{Guid.NewGuid():N}@x.com";
        await SeedPaidOrderAsync(emailA);
        await SeedPaidOrderAsync(emailB);

        await using var context = _fixture.CreateContext();
        var result = await CreateSut(context).ExecuteAsync(email: emailA[..4], page: 1, pageSize: 20);

        Assert.Single(result.Items);
        Assert.Equal(emailA, result.Items[0].Email);
    }

    [Fact]
    public async Task Pagination_bounds_page_size_and_reports_total()
    {
        var emails = new[]
        {
            $"p1-{Guid.NewGuid():N}@example.com",
            $"p2-{Guid.NewGuid():N}@example.com",
            $"p3-{Guid.NewGuid():N}@example.com",
        };
        foreach (var email in emails)
        {
            await SeedPaidOrderAsync(email);
        }

        await using var context = _fixture.CreateContext();
        var result = await CreateSut(context).ExecuteAsync(email: null, page: 1, pageSize: 2);

        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Total >= 3);
        Assert.Equal(2, result.PageSize);
    }

    [Fact]
    public async Task Order_item_exposes_only_the_assigned_key_count()
    {
        var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
        var orderId = await SeedPaidOrderWithAssignedKeyAsync(buyerEmail);

        await using var context = _fixture.CreateContext();
        var result = await CreateSut(context).ExecuteAsync(email: buyerEmail, page: 1, pageSize: 20);

        var order = Assert.Single(result.Items).Orders.Single(o => o.Id == orderId);
        var item = Assert.Single(order.Items);
        Assert.Equal(1, item.AssignedKeys);
    }

    [Fact]
    public async Task Order_item_exposes_the_revealed_key_count_once_revealed()
    {
        var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
        var orderId = await SeedPaidOrderWithRevealedKeyAsync(buyerEmail);

        await using var context = _fixture.CreateContext();
        var result = await CreateSut(context).ExecuteAsync(email: buyerEmail, page: 1, pageSize: 20);

        var order = Assert.Single(result.Items).Orders.Single(o => o.Id == orderId);
        var item = Assert.Single(order.Items);
        Assert.Equal(0, item.AssignedKeys);
        Assert.Equal(1, item.RevealedKeys);
    }

    private static ListBuyers CreateSut(AppDbContext context) => new(context);

    private async Task SeedPaidOrderAsync(string buyerEmail)
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null, buyerEmail, [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }

    private async Task SeedUnpaidOrderAsync(string buyerEmail)
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null, buyerEmail, [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }

    private async Task<Guid> SeedPaidOrderWithAssignedKeyAsync(string buyerEmail)
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null, buyerEmail, [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        var (blob, version) = cipher.Encrypt("CODE-ONE");
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
        context.Keys.Add(key); // client-generated Id — must be added explicitly (see AttachKeyToOrderItem)
        order.AttachKey(item.Id, key, now);
        await context.SaveChangesAsync();

        return order.Id;
    }

    private async Task<Guid> SeedPaidOrderWithRevealedKeyAsync(string buyerEmail)
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null, buyerEmail, [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        var (blob, version) = cipher.Encrypt("CODE-ONE");
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
        context.Keys.Add(key);
        order.AttachKey(item.Id, key, now);
        key.Reveal(now, "buyer-sub");
        await context.SaveChangesAsync();

        return order.Id;
    }
}
