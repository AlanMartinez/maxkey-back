using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Keys;

/// <summary>
/// An admin-loaded key code for a product variant, encrypted at rest
/// (fulfillment spec: Key Encryption at Rest — this type only stores the
/// encrypted bytes; encryption itself is an Application-layer concern).
/// Assigned to exactly one order item via <see cref="AssignTo"/> when attached
/// (design section 4.2 `AttachKey`).
/// </summary>
public sealed class Key : Entity
{
    public Guid ProductVariantId { get; private set; }
    public Guid? OrderItemId { get; private set; }
    public byte[] EncryptedCode { get; private set; }
    public short KeyVersion { get; private set; }
    public KeyStatus Status { get; private set; }
    public string LoadedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }

    public Key(Guid productVariantId, byte[] encryptedCode, short keyVersion, string loadedBy, DateTimeOffset now)
    {
        if (encryptedCode is null || encryptedCode.Length == 0)
        {
            throw new DomainException("Key encrypted code must not be empty.");
        }

        if (keyVersion < 1)
        {
            throw new DomainException("Key version must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(loadedBy))
        {
            throw new DomainException("Key loaded-by must not be empty.");
        }

        ProductVariantId = productVariantId;
        EncryptedCode = encryptedCode;
        KeyVersion = keyVersion;
        LoadedBy = loadedBy;
        Status = KeyStatus.Available;
        CreatedAt = now;
    }

    /// <summary>Assigns this key to an order item (design section 4.2 `AttachKey`).</summary>
    public void AssignTo(Guid orderItemId, DateTimeOffset now)
    {
        if (Status != KeyStatus.Available)
        {
            throw new DomainConflictException("Cannot assign a key that is not available.");
        }

        OrderItemId = orderItemId;
        Status = KeyStatus.Assigned;
        AssignedAt = now;
    }
}
