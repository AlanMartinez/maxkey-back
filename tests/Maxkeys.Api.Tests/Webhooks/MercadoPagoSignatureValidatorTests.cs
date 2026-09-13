using System.Security.Cryptography;
using System.Text;
using Maxkeys.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Tests.Webhooks;

/// <summary>
/// Pure unit tests for <see cref="MercadoPagoSignatureValidator"/> — no host or
/// database needed, since signature validation is zero-I/O (payments-webhook
/// spec "Signature Validation Before Any I/O").
/// </summary>
public sealed class MercadoPagoSignatureValidatorTests
{
    private const string Secret = "test-webhook-secret";

    [Fact]
    public void Valid_signature_is_accepted()
    {
        const string dataId = "123456";
        const string requestId = "req-1";
        const string ts = "1704908010";
        var header = $"ts={ts},v1={ComputeHex(dataId, requestId, ts)}";

        Assert.True(CreateValidator().IsValid(header, requestId, dataId));
    }

    [Fact]
    public void Tampered_v1_is_rejected()
    {
        const string dataId = "123456";
        const string requestId = "req-1";
        const string ts = "1704908010";
        var header = $"ts={ts},v1={new string('0', 64)}";

        Assert.False(CreateValidator().IsValid(header, requestId, dataId));
    }

    [Fact]
    public void Missing_signature_header_is_rejected()
    {
        Assert.False(CreateValidator().IsValid(null, "req-1", "123456"));
    }

    [Fact]
    public void Missing_request_id_is_rejected()
    {
        const string dataId = "123456";
        const string ts = "1704908010";
        var header = $"ts={ts},v1={ComputeHex(dataId, "req-1", ts)}";

        Assert.False(CreateValidator().IsValid(header, null, dataId));
    }

    [Fact]
    public void Data_id_is_lowercased_before_hashing()
    {
        const string dataId = "ABC123";
        const string requestId = "req-1";
        const string ts = "1704908010";
        var manifest = $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{ts};";
        var v1 = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(manifest)))
            .ToLowerInvariant();
        var header = $"ts={ts},v1={v1}";

        Assert.True(CreateValidator().IsValid(header, requestId, dataId));
    }

    private static MercadoPagoSignatureValidator CreateValidator() =>
        new(new TestOptionsMonitor<MercadoPagoOptions>(new MercadoPagoOptions { WebhookSecret = Secret }));

    private static string ComputeHex(string dataId, string requestId, string ts)
    {
        var manifest = $"id:{dataId};request-id:{requestId};ts:{ts};";
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(manifest)))
            .ToLowerInvariant();
    }
}

/// <summary>Minimal <see cref="IOptionsMonitor{TOptions}"/> stub returning a fixed value — no reload support needed in tests.</summary>
internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    public TestOptionsMonitor(T currentValue)
    {
        CurrentValue = currentValue;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
