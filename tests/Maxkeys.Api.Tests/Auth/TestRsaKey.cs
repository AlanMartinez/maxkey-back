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

    public static string JwksJson
    {
        get
        {
            var parameters = SigningKey.Rsa!.ExportParameters(false);
            var document = new
            {
                keys = new[]
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
                },
            };

            return JsonSerializer.Serialize(document);
        }
    }
}
