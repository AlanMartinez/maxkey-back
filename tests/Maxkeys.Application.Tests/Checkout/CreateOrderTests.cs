using Maxkeys.Application.Checkout;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Checkout;

[Collection(PostgresCollection.Name)]
public sealed class CreateOrderTests
{
    private readonly PostgresFixture _fixture;

    private static readonly IOptions<BankTransferOptions> EnabledBankTransfer =
        Options.Create(new BankTransferOptions { Alias = "chekeys.mp", AccountHolder = "Chekeys" });

    public CreateOrderTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Valid_checkout_persists_pending_order_and_payment_handoff()
    {
        var variantId = await SeedActiveVariantAsync(price: 5_000m, region: "AR", edition: "Deluxe");
        var gateway = new FakePaymentGateway
        {
            PreferenceToReturn = new("preference-123", "https://mp.test/checkout/preference-123")
        };

        await using var context = _fixture.CreateContext();
        var sut = new CreateOrder(context, gateway, EnabledBankTransfer);

        var result = await sut.ExecuteAsync(
            new CreateOrderRequest(null, "buyer@example.com", [new CreateOrderLine(variantId, 2)]));

        Assert.Equal("https://mp.test/checkout/preference-123", result.InitPoint);
        var gatewayOrder = Assert.Single(gateway.CreatePreferenceCalls);
        Assert.Equal(result.OrderId, gatewayOrder.Id);

        await using var readContext = _fixture.CreateContext();
        var order = await readContext.Orders.Include(order => order.Items).SingleAsync(order => order.Id == result.OrderId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal("preference-123", order.MpPreferenceId);
        Assert.Equal(10_000m, order.TotalAmount);
        var item = Assert.Single(order.Items);
        Assert.Equal(5_000m, item.UnitPrice);
        Assert.Equal("Checkout product", item.ProductNameSnapshot);
        Assert.Equal("AR · Deluxe", item.VariantNameSnapshot);
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public async Task Trusted_catalog_data_is_snapshotted_independently_of_client_values()
    {
        var variantId = await SeedActiveVariantAsync(price: 5_000m, region: "AR", edition: "Standard");
        var gateway = new FakePaymentGateway();

        await using var context = _fixture.CreateContext();
        var sut = new CreateOrder(context, gateway, EnabledBankTransfer);
        var result = await sut.ExecuteAsync(
            new CreateOrderRequest(null, "buyer@example.com", [new CreateOrderLine(variantId, 1)]));

        await using (var catalogChangeContext = _fixture.CreateContext())
        {
            var variant = await catalogChangeContext.ProductVariants.SingleAsync(candidate => candidate.Id == variantId);
            var product = await catalogChangeContext.Products.SingleAsync(candidate => candidate.Id == variant.ProductId);
            catalogChangeContext.Entry(product).Property(nameof(Product.Name)).CurrentValue = "Changed catalog product";
            catalogChangeContext.Entry(variant).Property(nameof(ProductVariant.Edition)).CurrentValue = "Changed edition";
            await catalogChangeContext.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateContext();
        var item = Assert.Single((await readContext.Orders.Include(order => order.Items)
            .SingleAsync(order => order.Id == result.OrderId)).Items);

        Assert.Equal(5_000m, item.UnitPrice);
        Assert.Equal("Checkout product", item.ProductNameSnapshot);
        Assert.Equal("AR · Standard", item.VariantNameSnapshot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_quantity_creates_nothing_and_does_not_call_gateway(int quantity)
    {
        var variantId = await SeedActiveVariantAsync(price: 100m, region: null, edition: null);
        var gateway = new FakePaymentGateway();
        var email = $"buyer-{Guid.NewGuid():N}@example.com";

        await using var context = _fixture.CreateContext();
        var sut = new CreateOrder(context, gateway, EnabledBankTransfer);

        await Assert.ThrowsAsync<DomainException>(() => sut.ExecuteAsync(
            new CreateOrderRequest(null, email, [new CreateOrderLine(variantId, quantity)])));

        Assert.Empty(gateway.CreatePreferenceCalls);
        await using var readContext = _fixture.CreateContext();
        Assert.Empty(await readContext.Orders.Where(order => order.BuyerEmail == email).ToListAsync());
    }

    [Fact]
    public async Task Missing_or_inactive_variant_creates_nothing_and_does_not_call_gateway()
    {
        var inactiveVariantId = await SeedActiveVariantAsync(price: 100m, region: null, edition: null, isActive: false);
        var gateway = new FakePaymentGateway();
        var email = $"buyer-{Guid.NewGuid():N}@example.com";

        await using var context = _fixture.CreateContext();
        var sut = new CreateOrder(context, gateway, EnabledBankTransfer);

        await Assert.ThrowsAsync<DomainException>(() => sut.ExecuteAsync(
            new CreateOrderRequest(null, email, [new CreateOrderLine(inactiveVariantId, 1)])));
        await Assert.ThrowsAsync<DomainException>(() => sut.ExecuteAsync(
            new CreateOrderRequest(null, email, [new CreateOrderLine(Guid.NewGuid(), 1)])));

        Assert.Empty(gateway.CreatePreferenceCalls);
        await using var readContext = _fixture.CreateContext();
        Assert.Empty(await readContext.Orders.Where(order => order.BuyerEmail == email).ToListAsync());
    }

    [Fact]
    public async Task Bank_transfer_checkout_awaits_validation_without_calling_the_gateway()
    {
        var variantId = await SeedActiveVariantAsync(price: 5_000m, region: "AR", edition: "Deluxe");
        var gateway = new FakePaymentGateway();

        await using var context = _fixture.CreateContext();
        var sut = new CreateOrder(context, gateway, EnabledBankTransfer);

        var result = await sut.ExecuteAsync(new CreateOrderRequest(
            null, "buyer@example.com", [new CreateOrderLine(variantId, 2)], PaymentMethod.BankTransfer, "+54 9 11 1234-5678"));

        Assert.Null(result.InitPoint);
        Assert.Empty(gateway.CreatePreferenceCalls);

        await using var readContext = _fixture.CreateContext();
        var order = await readContext.Orders.Include(order => order.Items).SingleAsync(order => order.Id == result.OrderId);
        Assert.Equal(OrderStatus.AwaitingTransferValidation, order.Status);
        Assert.Equal(PaymentMethod.BankTransfer, order.PaymentMethod);
        Assert.Equal("+5491112345678", order.BuyerPhone);
        Assert.Null(order.MpPreferenceId);
        Assert.Equal(10_000m, order.TotalAmount);
        Assert.Equal(5_000m, Assert.Single(order.Items).UnitPrice);

        var outboxEvent = Assert.Single(await new OutboxEventReader(readContext).ListByOrderIdAsync(result.OrderId));
        Assert.Equal(OutboxEventTypes.TransferSubmitted, outboxEvent.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1234")]
    public async Task Bank_transfer_with_invalid_phone_creates_nothing(string? phone)
    {
        var variantId = await SeedActiveVariantAsync(price: 100m, region: null, edition: null);
        var gateway = new FakePaymentGateway();
        var email = $"buyer-{Guid.NewGuid():N}@example.com";

        await using var context = _fixture.CreateContext();
        var sut = new CreateOrder(context, gateway, EnabledBankTransfer);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => sut.ExecuteAsync(
            new CreateOrderRequest(null, email, [new CreateOrderLine(variantId, 1)], PaymentMethod.BankTransfer, phone)));

        Assert.Equal("phone", exception.Field);
        Assert.Empty(gateway.CreatePreferenceCalls);
        await using var readContext = _fixture.CreateContext();
        Assert.Empty(await readContext.Orders.Where(order => order.BuyerEmail == email).ToListAsync());
    }

    [Fact]
    public async Task Bank_transfer_while_disabled_throws_unavailable_and_creates_nothing()
    {
        var variantId = await SeedActiveVariantAsync(price: 100m, region: null, edition: null);
        var gateway = new FakePaymentGateway();
        var email = $"buyer-{Guid.NewGuid():N}@example.com";

        await using var context = _fixture.CreateContext();
        var sut = new CreateOrder(context, gateway, Options.Create(new BankTransferOptions { Alias = "chekeys.mp" }));

        await Assert.ThrowsAsync<BankTransferUnavailableException>(() => sut.ExecuteAsync(
            new CreateOrderRequest(null, email, [new CreateOrderLine(variantId, 1)], PaymentMethod.BankTransfer, "1112345678")));

        Assert.Empty(gateway.CreatePreferenceCalls);
        await using var readContext = _fixture.CreateContext();
        Assert.Empty(await readContext.Orders.Where(order => order.BuyerEmail == email).ToListAsync());
    }

    [Fact]
    public async Task Mercado_pago_checkout_works_while_bank_transfer_is_disabled()
    {
        var variantId = await SeedActiveVariantAsync(price: 100m, region: null, edition: null);
        var gateway = new FakePaymentGateway();

        await using var context = _fixture.CreateContext();
        var sut = new CreateOrder(context, gateway, Options.Create(new BankTransferOptions()));

        var result = await sut.ExecuteAsync(
            new CreateOrderRequest(null, "buyer@example.com", [new CreateOrderLine(variantId, 1)]));

        Assert.NotNull(result.InitPoint);
        Assert.Single(gateway.CreatePreferenceCalls);
    }

    private async Task<Guid> SeedActiveVariantAsync(decimal price, string? region, string? edition, bool isActive = true)
    {
        await using var context = _fixture.CreateContext();
        var product = new Product($"checkout-{Guid.NewGuid():N}", "Checkout product", "steam", isActive: true);
        var variant = new ProductVariant(product.Id, price, "ARS", region: region, edition: edition, isActive: isActive);
        context.Products.Add(product);
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();
        return variant.Id;
    }
}
