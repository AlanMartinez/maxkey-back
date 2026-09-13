using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Maxkeys.Api.Tests.Auth;

/// <summary>
/// One fixed RSA key pair reused across JWKS tests, published as a single-key
/// JWKS document so <see cref="JwksApiTestFixture"/>'s in-process fake handler
/// can serve it without a real Supabase project (design section 11 testing
/// strategy: "JWKS mode with a test key pair served from an in-process
/// endpoint").
/// </summary>
internal static class TestRsaKey
{
    public const string Kid = "test-key-1";

    public static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = Kid };

    /// <summary>
    /// ES256 (P-256) key published alongside the RSA key. Supabase projects on
    /// asymmetric signing publish EC/ES256 keys, so the JWKS path must accept
    /// them as well as RS256 (task 10.0 evidence).
    /// </summary>
    public const string EcKid = "test-ec-key-1";

    public static readonly ECDsaSecurityKey EcSigningKey =
        new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = EcKid };

    public static string JwksJson
    {
        get
        {
            var parameters = SigningKey.Rsa!.ExportParameters(false);
            var ecParameters = EcSigningKey.ECDsa.ExportParameters(false);
            var document = new
            {
                keys = new object[]
                {
                    new
                    {
                        kty = "RSA",
                        kid = Kid,
                        use = "sig",
                        alg = "RS256",
                        n = Base64UrlEncoder.Encode(parameters.Modulus),
                        e = Base64UrlEncoder.Encode(parameters.Exponent),
                    },
                    new
                    {
                        kty = "EC",
                        kid = EcKid,
                        use = "sig",
                        alg = "ES256",
                        crv = "P-256",
                        x = Base64UrlEncoder.Encode(ecParameters.Q.X),
                        y = Base64UrlEncoder.Encode(ecParameters.Q.Y),
                    },
                },
            };

            return JsonSerializer.Serialize(document);
        }
    }
}
