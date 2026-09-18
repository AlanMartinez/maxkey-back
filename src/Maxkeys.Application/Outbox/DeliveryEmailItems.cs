using Maxkeys.Application.Notifications;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Builds the per-item, decrypted-key content for the admin-triggered delivery
/// resend (<see cref="OrderDeliveryResendHandler"/>) (design D1; admin-key-delivery-gate
/// spec: decision 4 — the only remaining sender, delivery no longer auto-emails). Decrypts each
/// assigned key in memory only — the caller must not persist the result.
/// </summary>
public static class DeliveryEmailItems
{
    public static IReadOnlyList<BuyerDeliveryItem> FromOrder(Order order, KeyCipher keyCipher) =>
        order.Items
            .Select(item => new BuyerDeliveryItem(
                item.ProductNameSnapshot,
                item.VariantNameSnapshot,
                item.Keys
                    .Where(key => key.Status == KeyStatus.Assigned)
                    .Select(key => keyCipher.Decrypt(key.EncryptedCode, key.KeyVersion))
                    .ToList()))
            .ToList();
}
