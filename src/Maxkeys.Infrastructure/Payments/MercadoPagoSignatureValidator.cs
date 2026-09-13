using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Maxkeys.Infrastructure.Payments;

/// <summary>
/// Validates the Mercado Pago <c>x-signature</c> header (payments-webhook spec
/// "Signature Validation Before Any I/O"). Performs zero I/O — the caller
/// (<c>WebhookEndpoints</c>) must reject the request with 401 on a
/// <see langword="false"/> result before touching the database or the gateway.
/// </summary>
public sealed class MercadoPagoSignatureValidator
{
    private readonly IOptionsMonitor<MercadoPagoOptions> _options;

    public MercadoPagoSignatureValidator(IOptionsMonitor<MercadoPagoOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Rebuilds the manifest <c>id:&lt;data.id&gt;;request-id:&lt;x-request-id&gt;;ts:&lt;ts&gt;;</c>
    /// (design section 6b) — <c>data.id</c> lowercased per Mercado Pago's documented
    /// signature manifest — and compares the computed HMAC-SHA256 hex digest against
    /// the <c>v1</c> segment of <paramref name="xSignature"/> using
    /// <see cref="CryptographicOperations.FixedTimeEquals"/>.
    /// </summary>
    public bool IsValid(string? xSignature, string? xRequestId, string? dataId)
    {
        if (string.IsNullOrEmpty(xSignature) || string.IsNullOrEmpty(xRequestId) || string.IsNullOrEmpty(dataId))
        {
            return false;
        }

        var (ts, v1) = ParseSignatureHeader(xSignature);
        if (ts is null || v1 is null)
        {
            return false;
        }

        var secret = _options.CurrentValue.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        var manifest = $"id:{dataId.ToLowerInvariant()};request-id:{xRequestId};ts:{ts};";
        var computedHash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest));
        var computedHex = Convert.ToHexString(computedHash).ToLowerInvariant();

        var computedHexBytes = Encoding.UTF8.GetBytes(computedHex);
        var providedHexBytes = Encoding.UTF8.GetBytes(v1);

        return computedHexBytes.Length == providedHexBytes.Length &&
               CryptographicOperations.FixedTimeEquals(computedHexBytes, providedHexBytes);
    }

    /// <summary>Parses the comma-separated <c>ts=...,v1=...</c> header value; order and extra segments are ignored.</summary>
    private static (string? Ts, string? V1) ParseSignatureHeader(string xSignature)
    {
        string? ts = null;
        string? v1 = null;

        foreach (var part in xSignature.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            var key = kv[0].Trim();
            var value = kv[1].Trim();

            if (key == "ts")
            {
                ts = value;
            }
            else if (key == "v1")
            {
                v1 = value;
            }
        }

        return (ts, v1);
    }
}
