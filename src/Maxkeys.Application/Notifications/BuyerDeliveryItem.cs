namespace Maxkeys.Application.Notifications;

/// <summary>
/// One order item's already-decrypted key codes for <see cref="EmailTemplates.BuyerOrderDelivered"/>.
/// Decryption happens in the caller (<c>OrderDeliveredHandler</c>) — templates never depend on <c>KeyCipher</c>.
/// </summary>
public sealed record BuyerDeliveryItem(string ProductName, string VariantName, IReadOnlyList<string> KeyCodes);
