using System.Net.Mail;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;

namespace Maxkeys.Domain.Orders;

/// <summary>Price-recomputed input line for <see cref="Order.Create"/> (cart-checkout spec: Server-Side Price Recomputation).</summary>
public sealed record OrderLine(
    Guid ProductVariantId,
    string ProductNameSnapshot,
    string VariantNameSnapshot,
    decimal UnitPrice,
    int Quantity);

/// <summary>A checkout order (design section 4.1/4.2).</summary>
public sealed class Order : Entity
{
    private readonly List<OrderItem> _items = new();

    public Guid? UserId { get; private set; }
    public string BuyerEmail { get; private set; }
    public OrderStatus Status { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }

    /// <summary>Normalized contact phone (see <see cref="NormalizePhone"/>). Required for, and only stored on, <see cref="PaymentMethod.BankTransfer"/> orders.</summary>
    public string? BuyerPhone { get; private set; }
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

    /// <summary>EF Core materialization constructor (ADR-01) — properties are set by the ORM via their private setters.</summary>
    private Order()
    {
        BuyerEmail = null!;
        Currency = null!;
    }

    private Order(Guid? userId, string buyerEmail, PaymentMethod paymentMethod, string? buyerPhone, DateTimeOffset now)
    {
        UserId = userId;
        BuyerEmail = buyerEmail;
        PaymentMethod = paymentMethod;
        BuyerPhone = buyerPhone;
        // A bank-transfer order skips the Mercado Pago handoff entirely: the buyer has
        // already been shown the account details, so it waits on an admin instead.
        Status = paymentMethod == PaymentMethod.BankTransfer ? OrderStatus.AwaitingTransferValidation : OrderStatus.Pending;
        Currency = "ARS";
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Creates a <see cref="OrderStatus.Pending"/> Mercado Pago order, or — for
    /// <see cref="PaymentMethod.BankTransfer"/> — an <see cref="OrderStatus.AwaitingTransferValidation"/>
    /// one, which requires a valid <paramref name="buyerPhone"/> so the operator can reach the
    /// buyer about the transfer. <paramref name="buyerPhone"/> is ignored for Mercado Pago.
    /// </summary>
    public static Order Create(
        Guid? userId,
        string buyerEmail,
        IReadOnlyCollection<OrderLine> lines,
        DateTimeOffset now,
        PaymentMethod paymentMethod = PaymentMethod.MercadoPago,
        string? buyerPhone = null)
    {
        if (string.IsNullOrWhiteSpace(buyerEmail))
        {
            throw new DomainException("Order buyer email must not be empty.");
        }

        // Trimmed before anything else stores or compares it: BuyerEmail is the ownership
        // key for a guest order (GetMyOrders claims by it), so a stray leading/trailing
        // space would leave the order permanently unclaimable and its keys unreachable.
        buyerEmail = buyerEmail.Trim();

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

        var phone = paymentMethod == PaymentMethod.BankTransfer ? NormalizePhone(buyerPhone) : null;

        var order = new Order(userId, buyerEmail, paymentMethod, phone, now);
        order._items.AddRange(lines.Select(line =>
            new OrderItem(line.ProductVariantId, line.ProductNameSnapshot, line.VariantNameSnapshot, line.UnitPrice, line.Quantity)));

        order.TotalAmount = order._items.Sum(item => item.UnitPrice * item.Quantity);
        return order;
    }

    /// <summary>
    /// Strips spaces, dashes and parentheses, keeps an optional leading <c>+</c>, and
    /// requires 8–15 digits (E.164 upper bound) — e.g. <c>"+54 (11) 1234-5678"</c> →
    /// <c>"+541112345678"</c>. Throws <see cref="DomainValidationException"/> on <c>phone</c>.
    /// </summary>
    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new DomainValidationException("phone", "Phone is required for bank transfer orders.");
        }

        var compact = new string(phone.Trim().Where(c => c is not (' ' or '-' or '(' or ')')).ToArray());
        var digits = compact.StartsWith('+') ? compact[1..] : compact;

        if (digits.Length is < 8 or > 15 || !digits.All(char.IsAsciiDigit))
        {
            throw new DomainValidationException("phone", "Phone must contain between 8 and 15 digits.");
        }

        return compact;
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

    /// <summary>
    /// Admin confirmation that a bank transfer arrived — the transfer counterpart of
    /// <see cref="MarkPaid"/>, without a Mercado Pago payment id. The caller raises the same
    /// <c>OrderApproved</c> outbox event as the Mercado Pago path, so fulfillment continues identically.
    /// </summary>
    public void ApproveTransfer(DateTimeOffset now)
    {
        if (Status != OrderStatus.AwaitingTransferValidation)
        {
            throw new DomainConflictException("Cannot approve a transfer for an order that is not awaiting transfer validation.");
        }

        Status = OrderStatus.Paid;
        PaidAt = now;
        UpdatedAt = now;
    }

    /// <summary>Admin rejection of a bank transfer that never arrived (or did not match): the order is cancelled.</summary>
    public void RejectTransfer(DateTimeOffset now)
    {
        if (Status != OrderStatus.AwaitingTransferValidation)
        {
            throw new DomainConflictException("Cannot reject a transfer for an order that is not awaiting transfer validation.");
        }

        Status = OrderStatus.Cancelled;
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

    /// <summary>
    /// Assigns <paramref name="key"/> to <paramref name="orderItemId"/> (fulfillment
    /// spec: Key Attachment). Once every item is complete the order transitions to
    /// <see cref="OrderStatus.KeysAssigned"/> and this method returns <c>true</c>
    /// (fulfillment spec: All-or-Nothing Delivery Derivation; admin-key-delivery-gate
    /// spec: decision 1 — MODIFIED, no longer reaches <see cref="OrderStatus.Delivered"/>
    /// on its own). An admin must call <see cref="MarkDelivered"/> to release the
    /// order to its buyer. Otherwise returns <c>false</c>.
    /// </summary>
    public bool AttachKey(Guid orderItemId, Key key, DateTimeOffset now)
    {
        if (Status != OrderStatus.AwaitingFulfillment)
        {
            throw new DomainConflictException("Cannot attach a key unless the order is awaiting fulfillment.");
        }

        var item = _items.FirstOrDefault(i => i.Id == orderItemId);
        if (item is null)
        {
            throw new DomainException("Order item does not belong to this order.");
        }

        if (key.ProductVariantId != item.ProductVariantId)
        {
            throw new DomainException("Key product variant does not match the order item.");
        }

        if (item.IsComplete)
        {
            throw new DomainConflictException("Order item already has all required keys assigned.");
        }

        key.AssignTo(item.Id, now);
        item.AddKey(key);
        UpdatedAt = now;

        if (!_items.All(i => i.IsComplete))
        {
            return false;
        }

        Status = OrderStatus.KeysAssigned;
        return true;
    }

    /// <summary>
    /// Admin action that releases an order's keys to its buyer (admin-key-delivery-gate
    /// spec: decision 1/3, "Entregar"). Valid only from <see cref="OrderStatus.KeysAssigned"/>.
    /// </summary>
    public void MarkDelivered(DateTimeOffset now)
    {
        if (Status != OrderStatus.KeysAssigned)
        {
            throw new DomainConflictException("Cannot deliver an order unless every item has its keys assigned.");
        }

        Status = OrderStatus.Delivered;
        DeliveredAt = now;
        UpdatedAt = now;
    }
}
