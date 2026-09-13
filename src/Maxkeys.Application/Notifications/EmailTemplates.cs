using System.Text;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Notifications;

/// <summary>
/// Plain-text email content builders (design section 3). Templates never
/// include key codes — <see cref="OperatorOrderAwaitingFulfillment"/> fires
/// while the order is still <see cref="OrderStatus.Paid"/>/transitioning to
/// <see cref="OrderStatus.AwaitingFulfillment"/>, before any key is attached.
/// </summary>
public static class EmailTemplates
{
    /// <summary>
    /// Internal operator notification sent when an order reaches
    /// <see cref="OrderStatus.AwaitingFulfillment"/> (outbox-processing spec:
    /// OrderApproved Handler). The buyer email is masked the same way as the
    /// public checkout status endpoint (<c>GetOrderStatus</c>) — this message
    /// may end up forwarded or logged, so it stays consistent with what a
    /// buyer-facing surface would already reveal.
    /// </summary>
    public static (string Subject, string TextBody) OperatorOrderAwaitingFulfillment(Order order)
    {
        var subject = $"Order {order.Id} awaiting fulfillment";

        var body = new StringBuilder()
            .AppendLine($"Order {order.Id} is awaiting fulfillment.")
            .AppendLine($"Buyer: {MaskEmail(order.BuyerEmail)}")
            .AppendLine($"Total: {order.TotalAmount} {order.Currency}")
            .AppendLine("Items:");

        foreach (var item in order.Items)
        {
            body.AppendLine($"  - {item.ProductNameSnapshot} ({item.VariantNameSnapshot}) x{item.Quantity}");
        }

        return (subject, body.ToString());
    }

    /// <summary>
    /// Buyer delivery email sent once per <see cref="OrderStatus.AwaitingFulfillment"/> →
    /// <see cref="OrderStatus.Delivered"/> transition (fulfillment spec: One-Time Delivery
    /// Email; outbox-processing spec, <c>OrderDeliveredHandler</c>). <paramref name="items"/>
    /// carry already-decrypted key codes — this template never touches <c>KeyCipher</c>.
    /// </summary>
    public static (string Subject, string TextBody) BuyerOrderDelivered(Order order, IReadOnlyList<BuyerDeliveryItem> items)
    {
        var subject = $"Your keys for order {order.Id}";

        var body = new StringBuilder()
            .AppendLine($"Thank you for your purchase! Here are your keys for order {order.Id}.")
            .AppendLine();

        foreach (var item in items)
        {
            body.AppendLine($"{item.ProductName} ({item.VariantName}):");
            foreach (var code in item.KeyCodes)
            {
                body.AppendLine($"  - {code}");
            }
        }

        return (subject, body.ToString());
    }

    /// <summary>Same masking rule as <c>Checkout.GetOrderStatus</c> — kept local since templates must not depend on Checkout.</summary>
    private static string MaskEmail(string email)
    {
        var separator = email.IndexOf('@');
        if (separator < 0)
        {
            return "***";
        }

        var localPart = email[..separator];
        return $"{(localPart.Length == 0 ? "***" : $"{localPart[0]}***")}{email[separator..]}";
    }
}
