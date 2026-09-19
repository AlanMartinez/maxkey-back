namespace Maxkeys.Domain.Keys;

/// <summary>Key lifecycle status (design section 4.2; admin-key-delivery-gate spec: decision 2 adds Revealed).</summary>
public enum KeyStatus
{
    Available,
    Assigned,
    Revealed
}
