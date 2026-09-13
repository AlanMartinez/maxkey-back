namespace Maxkeys.Domain.Common;

/// <summary>
/// Base type for all domain entities. <see cref="Id"/> is client-generated
/// (<see cref="Guid.NewGuid"/>) so an identity exists before the first
/// <c>SaveChanges</c> call — needed for Mercado Pago's <c>external_reference</c>
/// (design section 4.1).
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.NewGuid();
}
