namespace Maxkeys.Application.Security;

/// <summary>
/// Raised by <see cref="KeyCipher"/> for an unknown key version, a malformed
/// blob, or a failed AES-GCM tag verification. Never a <c>DomainException</c> —
/// this is an infrastructure/cryptography failure, not a business rule
/// violation, and callers MUST NOT put plaintext or key material in the message.
/// </summary>
public sealed class KeyCipherException : Exception
{
    public KeyCipherException(string message)
        : base(message)
    {
    }

    public KeyCipherException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
