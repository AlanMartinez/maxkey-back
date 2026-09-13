using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Maxkeys.Api.Tests.Auth;

/// <summary>
/// Covers PR10's JWT auth wiring in JWKS mode against an in-process fake JWKS
/// endpoint (design section 11 testing strategy; auth spec: Configurable JWT
/// Validation Mode, "Empty allowlist denies everyone").
/// </summary>
[Collection(JwksApiCollection.Name)]
public sealed class JwksAuthTests
{
    private readonly JwksApiTestFixture _factory;

    public JwksAuthTests(JwksApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Token_signed_with_a_published_key_is_accepted()
    {
        var token = TestTokens.CreateRs256(
            Guid.NewGuid().ToString(), JwksApiTestFixture.Issuer, JwksApiTestFixture.Audience, TestRsaKey.SigningKey);

        var response = await AuthorizedClient(token).GetAsync("/me/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_a_published_es256_key_is_accepted()
    {
        // Supabase asymmetric signing publishes EC/ES256 keys (verified against the
        // project's public JWKS for task 10.0), so this path must work end to end.
        var token = TestTokens.CreateEs256(
            Guid.NewGuid().ToString(), JwksApiTestFixture.Issuer, JwksApiTestFixture.Audience, TestRsaKey.EcSigningKey);

        var response = await AuthorizedClient(token).GetAsync("/me/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_an_unpublished_kid_is_rejected()
    {
        var unpublishedKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "unknown-kid" };
        var token = TestTokens.CreateRs256(
            Guid.NewGuid().ToString(), JwksApiTestFixture.Issuer, JwksApiTestFixture.Audience, unpublishedKey);

        var response = await AuthorizedClient(token).GetAsync("/me/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Empty_admin_allowlist_denies_even_a_valid_token()
    {
        var token = TestTokens.CreateRs256(
            Guid.NewGuid().ToString(), JwksApiTestFixture.Issuer, JwksApiTestFixture.Audience, TestRsaKey.SigningKey);

        var response = await AuthorizedClient(token).GetAsync("/admin/orders?status=AwaitingFulfillment");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient AuthorizedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
