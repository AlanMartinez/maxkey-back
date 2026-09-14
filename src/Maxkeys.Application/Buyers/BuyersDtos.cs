namespace Maxkeys.Application.Buyers;

/// <summary>One order item in the admin buyers view — exposes only the assigned-key count, never a key code (admin-buyers spec: Key Exposure in Buyer View).</summary>
public sealed record AdminBuyerOrderItem(string ProductName, string VariantName, int Quantity, int AssignedKeys);

/// <summary>One paid order for a buyer in the admin buyers view (admin-buyers spec: Buyer Listing Grouped By Email; design D4).</summary>
public sealed record AdminBuyerOrder(
    Guid Id, string Status, DateTimeOffset? PaidAt, decimal TotalAmount, string Currency, IReadOnlyList<AdminBuyerOrderItem> Items);

/// <summary>One buyer entry grouped by email, with every paid order nested underneath (admin-buyers spec: Buyer Listing Grouped By Email).</summary>
public sealed record AdminBuyer(string Email, int OrderCount, DateTimeOffset? LastPaidAt, IReadOnlyList<AdminBuyerOrder> Orders);

/// <summary>Paginated result of <see cref="ListBuyers"/> (design D4 contract table).</summary>
public sealed record BuyersPage(IReadOnlyList<AdminBuyer> Items, int Page, int PageSize, int Total);
