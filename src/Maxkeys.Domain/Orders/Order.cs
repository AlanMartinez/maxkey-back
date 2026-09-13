using System.Net.Mail;
using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Orders;

/// <summary>Price-recomputed input line for <see cref="Order.Create"/> (cart-checkout spec: Server-Side Price Recomputation).</summary>
public sealed record OrderLine(
    Guid ProductVariantId,
    string ProductNameSnapshot,
    string VariantNameSnapshot,
    decimal UnitPrice,
    int Quantity);

/// <summary>A checkout order (design section 4.1/4.2). `AttachKey`/`Delivered` derivation added in PR2b.</summary>
public sealed class Order : Entity
{
    private readonly List<OrderItem> _items = new();

    public Guid? UserId { get; private set; }
    public string BuyerEmail { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; }
    public string? MpPreferenceId { get; private set; }
    public string? MpPaymentId { get; private set; }
    public string? LastPaymentAttemptId { get; private set; }
    public string? LastPaymentAttemptStatus { get; private set; }
    public DateTimeOffset? LastPaymentAttemptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<OrderItem> Items => _items;

    private Order(Guid? userId, string buyerEmail, DateTimeOffset now)
    {
        UserId = userId;
        BuyerEmail = buyerEmail;
        Status = OrderStatus.Pending;
        Currency = "ARS";
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Order Create(Guid? userId, string buyerEmail, IReadOnlyCollection<OrderLine> lines, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(buyerEmail))
        {
            throw new DomainException("Order buyer email must not be empty.");
        }

        try
        {
            _ = new MailAddress(buyerEmail);
        }
        catch (FormatException)
        {
            throw new DomainException("Order buyer email must be a valid email address.");
        }

        if (lines is null || lines.Count == 0)
        {
            throw new DomainException("Order must contain at least one item.");
        }

        if (lines.Count > 20)
        {
            throw new DomainException("Order must not contain more than 20 items.");
        }

        var order = new Order(userId, buyerEmail, now);
        order._items.AddRange(lines.Select(line =>
            new OrderItem(line.ProductVariantId, line.ProductNameSnapshot, line.VariantNameSnapshot, line.UnitPrice, line.Quantity)));

        order.TotalAmount = order._items.Sum(item => item.UnitPrice * item.Quantity);
        return order;
    }

    public void AttachPreference(string preferenceId)
    {
        if (string.IsNullOrWhiteSpace(preferenceId))
        {
            throw new DomainException("Preference id must not be empty.");
        }

        if (Status != OrderStatus.Pending || MpPreferenceId is not null)
        {
            throw new DomainConflictException("Cannot attach a preference to this order.");
        }

        MpPreferenceId = preferenceId;
    }

    public void RecordPaymentAttempt(string paymentId, string mpStatus, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new DomainException("Payment id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(mpStatus))
        {
            throw new DomainException("Payment status must not be empty.");
        }

        if (Status != OrderStatus.Pending)
        {
            throw new DomainConflictException("Cannot record a payment attempt on a non-pending order.");
        }

        LastPaymentAttemptId = paymentId;
        LastPaymentAttemptStatus = mpStatus;
        LastPaymentAttemptAt = now;
        UpdatedAt = now;
    }

    public void MarkPaid(string paymentId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new DomainException("Payment id must not be empty.");
        }

        if (Status != OrderStatus.Pending)
        {
            throw new DomainConflictException("Cannot mark a non-pending order as paid.");
        }

        Status = OrderStatus.Paid;
        PaidAt = now;
        MpPaymentId = paymentId;
        LastPaymentAttemptId = paymentId;
        LastPaymentAttemptStatus = "approved";
        LastPaymentAttemptAt = now;
        UpdatedAt = now;
    }

    public void MarkAwaitingFulfillment(DateTimeOffset now)
    {
        if (Status != OrderStatus.Paid)
        {
            throw new DomainConflictException("Cannot mark a non-paid order as awaiting fulfillment.");
        }

        Status = OrderStatus.AwaitingFulfillment;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new DomainConflictException("Cannot cancel a non-pending order.");
        }

        Status = OrderStatus.Cancelled;
        UpdatedAt = now;
    }
}
