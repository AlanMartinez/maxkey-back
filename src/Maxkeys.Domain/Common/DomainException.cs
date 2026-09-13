namespace Maxkeys.Domain.Common;

/// <summary>
/// The request violates a business rule regardless of the entity's current
/// state (e.g. inactive variant, invalid email, quantity out of range).
/// Maps to HTTP 422 (design section 4.2, section 7).
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
