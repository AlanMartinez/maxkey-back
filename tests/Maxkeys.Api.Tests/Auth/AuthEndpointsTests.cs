using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Auth;

/// <summary>
/// Covers PR10's JWT auth wiring in HS256 mode (auth spec: Configurable JWT
/// Validation Mode, Anonymous Access to Guest-Eligible Endpoints, Admin
/// Authorization Policy, User Identity Linking; orders-history spec:
/// Ownership Enforcement; design section 6e <c>OptionalBearerFilter</c>).
/// </summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AuthEndpointsTests
{
    private const string OtherSub = "22222222-2222-2222-2222-222222222222";

    private readonly Hs256ApiTestFixture _factory;

    public AuthEndpointsTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Valid_token_grants_access_to_me_orders()
    {
        var response = await AuthorizedClient(Token(OtherSub)).GetAsync("/me/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_to_me_orders_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/me/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_audience_is_rejected()
    {
        var token = TestTokens.CreateHs256(OtherSub, Hs256ApiTestFixture.Issuer, "some-other-audience", Hs256ApiTestFixture.Hs256Secret);
        var response = await AuthorizedClient(token).GetAsync("/me/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = TestTokens.CreateHs256(
            OtherSub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret,
            lifetime: TimeSpan.FromMinutes(-5));

        var response = await AuthorizedClient(token).GetAsync("/me/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_token_is_rejected()
    {
        var response = await AuthorizedClient("not-a-jwt").GetAsync("/me/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden_from_admin_endpoint()
    {
        var response = await AuthorizedClient(Token(OtherSub)).GetAsync("/admin/orders?status=AwaitingFulfillment");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_sub_is_allowed_into_admin_endpoint()
    {
        var response = await AuthorizedClient(Token(Hs256ApiTestFixture.AdminSub)).GetAsync("/admin/orders?status=AwaitingFulfillment");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Order_owned_by_another_user_returns_404()
    {
        var ownerId = Guid.NewGuid();
        var orderId = await SeedOrderAsync(ownerId);

        var response = await AuthorizedClient(Token(OtherSub)).GetAsync($"/me/orders/{orderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_checkout_is_allowed_as_a_guest()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/checkout/orders", new
        {
            email = $"guest-{Guid.NewGuid():N}@example.com",
            items = Array.Empty<object>(),
        });

        // An empty item list is a 422 from CreateOrder, but the point of this test is that it is
        // never a 401 — a 401 here would mean OptionalBearerFilter rejected a headerless guest.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_with_an_invalid_bearer_is_rejected_instead_of_falling_back_to_guest()
    {
        var response = await AuthorizedClient("not-a-jwt").PostAsJsonAsync("/checkout/orders", new
        {
            email = $"guest-{Guid.NewGuid():N}@example.com",
            items = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string Token(string sub) =>
        TestTokens.CreateHs256(sub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);

    private HttpClient AuthorizedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> SeedOrderAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(
            userId, $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(Guid.NewGuid(), "Product", "Standard", 100m, 1)], DateTimeOffset.UtcNow);

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }
}
