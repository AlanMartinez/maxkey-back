using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Tests.Auth;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>
/// Covers <c>GET /admin/me</c> (design D5, admin-catalog spec "Admin Catalog
/// Authorization" — same <see cref="AdminPolicy"/> semantics, verified here
/// through the dedicated status-check endpoint the frontend guard calls).
/// </summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AdminMeEndpointTests
{
    private const string NonAdminSub = "33333333-3333-3333-3333-333333333333";

    private readonly Hs256ApiTestFixture _factory;

    public AdminMeEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/admin/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden()
    {
        var response = await AuthorizedClient(NonAdminSub).GetAsync("/admin/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_sub_receives_its_own_sub()
    {
        var response = await AuthorizedClient(Hs256ApiTestFixture.AdminSub).GetAsync("/admin/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AdminMeResponse>();
        Assert.Equal(Hs256ApiTestFixture.AdminSub, body!.Sub);
    }

    private HttpClient AuthorizedClient(string sub)
    {
        var token = TestTokens.CreateHs256(sub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
