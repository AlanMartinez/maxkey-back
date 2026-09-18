using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;

namespace Maxkeys.Domain.Tests.Keys;

public class KeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Code = { 1, 2, 3 };

    [Fact]
    public void Constructor_WithValidData_SetsFieldsAsAvailable()
    {
        var variantId = Guid.NewGuid();
        var key = new Key(variantId, Code, 1, "admin@example.com", Now);
        Assert.Equal(variantId, key.ProductVariantId);
        Assert.Equal(KeyStatus.Available, key.Status);
        Assert.Null(key.OrderItemId);
        Assert.Null(key.AssignedAt);
        Assert.Equal(Now, key.CreatedAt);
    }

    [Fact]
    public void Constructor_WithEmptyCode_Throws() =>
        Assert.Throws<DomainException>(() => new Key(Guid.NewGuid(), Array.Empty<byte>(), 1, "admin@example.com", Now));

    [Fact]
    public void Constructor_WithVersionBelowOne_Throws() =>
        Assert.Throws<DomainException>(() => new Key(Guid.NewGuid(), Code, 0, "admin@example.com", Now));

    [Fact]
    public void Constructor_WithEmptyLoadedBy_Throws() =>
        Assert.Throws<DomainException>(() => new Key(Guid.NewGuid(), Code, 1, "", Now));

    [Fact]
    public void AssignTo_WhenAvailable_SetsAssignedAndOrderItemId()
    {
        var key = new Key(Guid.NewGuid(), Code, 1, "admin@example.com", Now);
        var itemId = Guid.NewGuid();
        key.AssignTo(itemId, Now.AddMinutes(1));
        Assert.Equal(KeyStatus.Assigned, key.Status);
        Assert.Equal(itemId, key.OrderItemId);
        Assert.Equal(Now.AddMinutes(1), key.AssignedAt);
    }

    [Fact]
    public void AssignTo_WhenAlreadyAssigned_Throws()
    {
        var key = new Key(Guid.NewGuid(), Code, 1, "admin@example.com", Now);
        key.AssignTo(Guid.NewGuid(), Now.AddMinutes(1));
        Assert.Throws<DomainConflictException>(() => key.AssignTo(Guid.NewGuid(), Now.AddMinutes(2)));
    }

    [Fact]
    public void Reveal_WhenAssigned_SetsRevealedAndTimestamp()
    {
        var key = new Key(Guid.NewGuid(), Code, 1, "admin@example.com", Now);
        key.AssignTo(Guid.NewGuid(), Now.AddMinutes(1));

        key.Reveal(Now.AddMinutes(2));

        Assert.Equal(KeyStatus.Revealed, key.Status);
        Assert.Equal(Now.AddMinutes(2), key.RevealedAt);
    }

    [Fact]
    public void Reveal_WhenAvailable_Throws()
    {
        var key = new Key(Guid.NewGuid(), Code, 1, "admin@example.com", Now);
        Assert.Throws<DomainConflictException>(() => key.Reveal(Now.AddMinutes(1)));
    }

    [Fact]
    public void Reveal_WhenAlreadyRevealed_Throws()
    {
        var key = new Key(Guid.NewGuid(), Code, 1, "admin@example.com", Now);
        key.AssignTo(Guid.NewGuid(), Now.AddMinutes(1));
        key.Reveal(Now.AddMinutes(2));
        Assert.Throws<DomainConflictException>(() => key.Reveal(Now.AddMinutes(3)));
    }
}
