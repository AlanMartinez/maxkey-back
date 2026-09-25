using System.Net;
using System.Text;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Notifications;

/// <summary>
/// Buyer and operator email content builders (design section 3). Templates never
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
    /// Email — MODIFIED, admin-key-delivery-gate spec: now sent only via the admin-triggered
    /// <c>OrderDeliveryResendHandler</c>). <paramref name="items"/>
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

    /// <summary>
    /// Automatic buyer notification sent once an order reaches
    /// <see cref="OrderStatus.Delivered"/> (admin-key-delivery-gate spec:
    /// decision 4 revisited — <c>OrderDeliveredHandler</c>). Unlike
    /// <see cref="BuyerOrderDelivered"/> (the admin-triggered resend), this
    /// never includes key codes — it lists purchased items and points the buyer at
    /// <paramref name="accountOrdersUrl"/>, where <c>RevealOrderItemKeys</c>
    /// is the sole path that discloses a code (ADR-14).
    /// </summary>
    public static (string Subject, string TextBody, string HtmlBody) BuyerOrderReady(Order order, string accountOrdersUrl)
    {
        var primaryProduct = order.Items[0].ProductNameSnapshot;
        var subject = $"Tus claves de {primaryProduct} ya están listas";
        var logoUrl = new Uri(new Uri(accountOrdersUrl), "/images/logo/logo.png").AbsoluteUri;

        var body = new StringBuilder()
            .AppendLine("¡Gracias por tu compra!")
            .AppendLine($"Tus claves de {primaryProduct} ya están disponibles.")
            .AppendLine()
            .AppendLine("Productos de tu pedido:");

        foreach (var item in order.Items)
        {
            body.AppendLine($"- {item.ProductNameSnapshot} ({item.VariantNameSnapshot}) x{item.Quantity}");
        }

        body.AppendLine()
            .AppendLine("Iniciá sesión con Google para recibir tus claves:")
            .AppendLine(accountOrdersUrl);

        var html = new StringBuilder()
            .AppendLine("<!doctype html>")
            .AppendLine("<html><body style=\"font-family:Arial,sans-serif;color:#1f2937;line-height:1.5\">")
            .AppendLine($"<img src=\"{WebUtility.HtmlEncode(logoUrl)}\" alt=\"Chekeys\" style=\"display:block;width:120px;height:auto;margin:0 0 24px\" />")
            .AppendLine("<p>¡Gracias por tu compra!</p>")
            .AppendLine($"<p>Tus claves de <strong>{WebUtility.HtmlEncode(primaryProduct)}</strong> ya están disponibles.</p>")
            .AppendLine("<p><strong>Productos de tu pedido:</strong></p><ul>");

        foreach (var item in order.Items)
        {
            html.AppendLine($"<li>{WebUtility.HtmlEncode(item.ProductNameSnapshot)} ({WebUtility.HtmlEncode(item.VariantNameSnapshot)}) x{item.Quantity}</li>");
        }

        html.AppendLine("</ul>")
            .AppendLine($"<p>Iniciá sesión con Google para recibir tus claves: <a href=\"{WebUtility.HtmlEncode(accountOrdersUrl)}\">Ver mis pedidos</a></p>")
            .AppendLine("</body></html>");

        return (subject, body.ToString(), html.ToString());
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
