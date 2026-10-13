namespace Maxkeys.Domain.Common;

/// <summary>
/// A <see cref="DomainException"/> tied to one caller-supplied input field
/// (e.g. <c>phone</c>), so the API can report it under <c>errors.{field}</c> in
/// the 422 Problem Details body. <see cref="Field"/> uses the request's JSON
/// (camelCase) member name.
/// </summary>
public class DomainValidationException : DomainException
{
    public DomainValidationException(string field, string message)
        : base(message)
    {
        Field = field;
    }

    public string Field { get; }
}
