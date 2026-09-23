using Maxkeys.Application.Notifications;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Tests.Notifications;

public sealed class EmailTemplatesTests
{
    [Fact]
    public void BuyerOrderReady_uses_primary_product_and_lists_every_item_without_internal_order_id()
    {
        var order = Order.Create(
            null,
            "buyer@example.com",
            [
                new OrderLine(Guid.NewGuid(), "EA SPORTS FC 26", "Ultimate Edition", 70_000m, 1),
                new OrderLine(Guid.NewGuid(), "Xbox Game Pass", "3 meses", 20_000m, 2),
            ],
            DateTimeOffset.UtcNow);

        var (subject, textBody, htmlBody) = EmailTemplates.BuyerOrderReady(
            order,
            "https://chekeys.com/account/orders");

        Assert.Equal("Tus claves de EA SPORTS FC 26 ya están listas", subject);
        Assert.DoesNotContain(order.Id.ToString(), subject);
        Assert.Contains("EA SPORTS FC 26 (Ultimate Edition) x1", textBody);
        Assert.Contains("Xbox Game Pass (3 meses) x2", textBody);
        Assert.Contains("Iniciá sesión con Google para recibir tus claves", textBody);
        Assert.DoesNotContain(order.Id.ToString(), textBody);
        Assert.NotNull(htmlBody);
        Assert.Contains("https://chekeys.com/images/logo/logo.png", htmlBody);
        Assert.True(htmlBody.IndexOf("<img", StringComparison.Ordinal) < htmlBody.IndexOf("¡Gracias por tu compra!", StringComparison.Ordinal));
        Assert.DoesNotContain(order.Id.ToString(), htmlBody);
    }
}
