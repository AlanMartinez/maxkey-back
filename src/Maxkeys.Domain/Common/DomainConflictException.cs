namespace Maxkeys.Domain.Common;

/// <summary>
/// The request is valid but the entity's current state does not allow it
/// (wrong status, item already full, concurrency loser). Maps to HTTP 409
/// (design section 4.2, section 7).
/// </summary>
public class DomainConflictException : DomainException
{
    public DomainConflictException(string message)
        : base(message)
    {
    }
}
