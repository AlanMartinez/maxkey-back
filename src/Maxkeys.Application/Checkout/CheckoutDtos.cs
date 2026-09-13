using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Checkout;

/// <summary>One cart line accepted by checkout. Catalog snapshots and prices are always resolved server-side.</summary>
public sealed record CreateOrderLine(Guid VariantId, int Quantity);

/// <summary>Buyer identity and variant/quantity-only cart input for a pending checkout.</summary>
public sealed record CreateOrderRequest(
    Guid? UserId,
    string Email,
    IReadOnlyList<CreateOrderLine> Items);

/// <summary>Durable order reference and the payment gateway handoff URL.</summary>
public sealed record CreateOrderResult(Guid OrderId, string InitPoint);

/// <summary>Privacy-safe persisted state used by the checkout result flow.</summary>
public sealed record CheckoutStatusResult(
    Guid OrderId,
    OrderStatus Status,
    string BuyerEmail,
    string? LastPaymentAttemptId,
    string? LastPaymentAttemptStatus,
    DateTimeOffset? LastPaymentAttemptAt,
    decimal TotalAmount,
    string Currency);
