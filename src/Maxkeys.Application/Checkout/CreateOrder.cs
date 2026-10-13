using Maxkeys.Application.Payments;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Checkout;

/// <summary>
/// Creates a pending checkout from active catalog data. Prices and display snapshots
/// are never accepted from the caller, which keeps the order immutable when the
/// catalog changes later.
/// <para>
/// A <see cref="PaymentMethod.BankTransfer"/> checkout runs the same recomputation but
/// never calls the payment gateway: the order is stored
/// <see cref="OrderStatus.AwaitingTransferValidation"/> together with a
/// <see cref="OutboxEventTypes.TransferSubmitted"/> event (operator email) in one
/// <c>SaveChangesAsync</c>, and the result carries no init point.
/// </para>
/// </summary>
public sealed class CreateOrder
{
    private readonly IAppDbContext _db;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IOptions<BankTransferOptions> _bankTransferOptions;

    public CreateOrder(IAppDbContext db, IPaymentGateway paymentGateway, IOptions<BankTransferOptions> bankTransferOptions)
    {
        _db = db;
        _paymentGateway = paymentGateway;
        _bankTransferOptions = bankTransferOptions;
    }

    public async Task<CreateOrderResult> ExecuteAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PaymentMethod == PaymentMethod.BankTransfer && !_bankTransferOptions.Value.IsEnabled)
        {
            throw new BankTransferUnavailableException();
        }

        var requestedItems = request.Items ?? throw new DomainException("Order items must not be null.");
        var variantIds = requestedItems.Select(item => item.VariantId).Distinct().ToList();

        var variants = await _db.ProductVariants
            .Where(variant => variant.IsActive && variantIds.Contains(variant.Id))
            .ToListAsync(cancellationToken);
        var products = await _db.Products
            .Where(product => product.IsActive && variants.Select(variant => variant.ProductId).Contains(product.Id))
            .ToListAsync(cancellationToken);

        var variantsById = variants
            .Join(products, variant => variant.ProductId, product => product.Id, (variant, product) => new { variant, product })
            .ToDictionary(pair => pair.variant.Id);

        var lines = requestedItems.Select(item =>
        {
            if (!variantsById.TryGetValue(item.VariantId, out var catalogItem))
            {
                throw new DomainException("Order contains an unknown or inactive product variant.");
            }

            return new OrderLine(
                catalogItem.variant.Id,
                catalogItem.product.Name,
                BuildVariantName(catalogItem.variant.Region, catalogItem.variant.Edition),
                catalogItem.variant.Price,
                item.Quantity);
        }).ToList();

        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(request.UserId, request.Email, lines, now, request.PaymentMethod, request.Phone);
        _db.Orders.Add(order);

        if (order.PaymentMethod == PaymentMethod.BankTransfer)
        {
            _db.OutboxEvents.Add(new OutboxEvent(OutboxEventTypes.TransferSubmitted, $$"""{"orderId":"{{order.Id}}"}""", now));
            await _db.SaveChangesAsync(cancellationToken);
            return new CreateOrderResult(order.Id, InitPoint: null);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var preference = await _paymentGateway.CreatePreferenceAsync(order, cancellationToken);
        order.AttachPreference(preference.PreferenceId);
        await _db.SaveChangesAsync(cancellationToken);

        return new CreateOrderResult(order.Id, preference.InitPoint);
    }

    private static string BuildVariantName(string? region, string? edition)
    {
        if (!string.IsNullOrWhiteSpace(region) && !string.IsNullOrWhiteSpace(edition))
        {
            return $"{region} · {edition}";
        }

        return edition ?? region ?? "Estándar";
    }
}
