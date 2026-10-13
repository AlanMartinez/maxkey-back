namespace Maxkeys.Domain.Orders;

/// <summary>
/// Order lifecycle status (design section 4.2). `KeysAssigned` and `Delivered` are derived/set by `Order.AttachKey`/`Order.MarkDelivered`.
/// `AwaitingTransferValidation` is the bank-transfer counterpart of `Pending`: the buyer says they paid, and an admin
/// confirms (`Order.ApproveTransfer` → `Paid`) or rejects (`Order.RejectTransfer` → `Cancelled`) it. Stored as text,
/// so the member order carries no persisted meaning; new members are still appended at the end.
/// </summary>
public enum OrderStatus
{
    Pending,
    Paid,
    AwaitingFulfillment,
    Delivered,
    Cancelled,
    KeysAssigned,
    AwaitingTransferValidation
}
