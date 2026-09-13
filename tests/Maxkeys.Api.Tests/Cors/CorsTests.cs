using Maxkeys.Api.Tests.Fixtures;

namespace Maxkeys.Api.Tests.Cors;

/// <summary>
/// Task 9.10: a request from an origin not in <c>Cors:AllowedOrigins</c> gets no
/// <c>Access-Control-Allow-Origin</c> header; an allowed origin does. No wildcard
/// origin is ever registered (design §10/§12, ADR-18 CORS consequence).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CorsTests
{
    private readonly ApiTestFixture _factory;

    public CorsTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Disallowed_origin_receives_no_allow_origin_header()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", "http://not-allowed.test");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Allowed_origin_receives_the_allow_origin_header()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", ApiTestFixture.AllowedOrigin);

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal(ApiTestFixture.AllowedOrigin, Assert.Single(values!));
    }
}
