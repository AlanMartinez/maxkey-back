using Maxkeys.Application.Payments;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IPaymentGateway"/> for Application tests (design section
/// 11). Records every call so tests can assert on what was sent, and returns
/// configurable results or throws a configured <see cref="Exception"/>.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public List<Order> CreatePreferenceCalls { get; } = [];

    public List<string> GetPaymentCalls { get; } = [];

    public PaymentPreference PreferenceToReturn { get; set; } =
        new("fake-preference-id", "https://mp.test/checkout/fake-preference-id");

    public PaymentInfo? PaymentToReturn { get; set; }

    /// <summary>When set, both methods throw this instead of returning.</summary>
    public Exception? ExceptionToThrow { get; set; }

    public Task<PaymentPreference> CreatePreferenceAsync(Order order, CancellationToken cancellationToken = default)
    {
        CreatePreferenceCalls.Add(order);

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(PreferenceToReturn);
    }

    public Task<PaymentInfo> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        GetPaymentCalls.Add(paymentId);

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        if (PaymentToReturn is null)
        {
            throw new InvalidOperationException(
                $"{nameof(FakePaymentGateway)}.{nameof(PaymentToReturn)} was not configured for this test.");
        }

        return Task.FromResult(PaymentToReturn);
    }
}
