using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Checkout;

/// <summary>Reads persisted checkout state without contacting the payment gateway.</summary>
public sealed class GetOrderStatus
{
    private readonly IAppDbContext _db;

    public GetOrderStatus(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<CheckoutStatusResult?> ExecuteAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        return new CheckoutStatusResult(
            order.Id,
            order.Status,
            MaskEmail(order.BuyerEmail),
            order.LastPaymentAttemptId,
            order.LastPaymentAttemptStatus,
            order.LastPaymentAttemptAt);
    }

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
