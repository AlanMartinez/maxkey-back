using System.Net;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.DevPayments;

/// <summary>Local demo mode end-to-end (docs/local-demo.md): Development host with <c>Payments:Mode=Fake</c>.</summary>
[Collection(FakePaymentsApiCollection.Name)]
public sealed class DevPaymentEndpointsTests
{
    private readonly FakePaymentsApiTestFixture _factory;

    public DevPaymentEndpointsTests(FakePaymentsApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Guest_checkout_returns_an_init_point_under_dev_payments()
    {
        var client = _factory.CreateClient();

        var (orderId, initPoint) = await CreateOrder(client);

        Assert.Equal($"{FakePaymentsApiTestFixture.InitPointBaseUrl}/dev/payments/{orderId}", initPoint);
    }

    [Fact]
    public async Task Fake_payment_page_renders_html_with_the_order_id_and_approve_form()
    {
        var client = _factory.CreateClient();
        var (orderId, _) = await CreateOrder(client);

        var response = await client.GetAsync($"/dev/payments/{orderId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(orderId.ToString(), html);
        Assert.Contains($"action=\"/dev/payments/{orderId}/approve\"", html);
        Assert.Contains("Approve payment", html);
    }

    [Fact]
    public async Task Fake_payment_page_for_an_unknown_order_returns_404_problem_details()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/dev/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Approving_the_fake_payment_redirects_303_and_moves_the_order_past_pending()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (orderId, _) = await CreateOrder(client);

        var approve = await client.PostAsync($"/dev/payments/{orderId}/approve", content: null);

        Assert.Equal(HttpStatusCode.SeeOther, approve.StatusCode);
        Assert.Equal(
            FakePaymentsApiTestFixture.ReturnUrlTemplate.Replace("{orderId}", orderId.ToString()),
            approve.Headers.Location?.ToString());

        var status = await client.GetFromJsonAsync<CheckoutOrderStatusResponse>($"/checkout/orders/{orderId}/status");

        Assert.NotNull(status);
        // ProcessPaymentNotification marks the order Paid; the outbox processor may already have
        // advanced it to AwaitingFulfillment by the time the status is read.
        Assert.Contains(status.Status, new[] { "Paid", "AwaitingFulfillment" });
        Assert.Equal("approved", status.LastPaymentAttemptStatus);
    }

    [Fact]
    public async Task Approving_twice_is_idempotent_and_still_redirects()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (orderId, _) = await CreateOrder(client);

        await client.PostAsync($"/dev/payments/{orderId}/approve", content: null);
        var second = await client.PostAsync($"/dev/payments/{orderId}/approve", content: null);

        Assert.Equal(HttpStatusCode.SeeOther, second.StatusCode);
    }

    private async Task<(Guid OrderId, string InitPoint)> CreateOrder(HttpClient client)
    {
        var variantId = await SeedActiveVariant();

        var response = await client.PostAsJsonAsync("/checkout/orders", new
        {
            email = $"buyer-{Guid.NewGuid():N}@example.com",
            items = new[] { new { variantId, quantity = 1 } },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CheckoutOrderResponse>();
        Assert.NotNull(body);

        return (body.OrderId, body.InitPoint);
    }

    private async Task<Guid> SeedActiveVariant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product($"p-{Guid.NewGuid():N}", "Fake Payment Test Product", $"platform-{Guid.NewGuid():N}");
        db.Products.Add(product);
        var variant = new ProductVariant(product.Id, 100m, "ARS");
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        return variant.Id;
    }
}

/// <summary>Default Development host with <c>Payments:Mode</c> empty: the demo routes must not exist.</summary>
[Collection(ApiCollection.Name)]
public sealed class DevPaymentEndpointsDisabledByModeTests
{
    private readonly ApiTestFixture _factory;

    public DevPaymentEndpointsDisabledByModeTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Fake_payment_page_is_404_when_mode_is_not_fake()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/dev/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>Production host with <c>Payments:Mode=Fake</c>: the setting is inert outside Development.</summary>
[Collection(FakePaymentsProductionApiCollection.Name)]
public sealed class DevPaymentEndpointsDisabledByEnvironmentTests
{
    private readonly FakePaymentsProductionApiTestFixture _factory;

    public DevPaymentEndpointsDisabledByEnvironmentTests(FakePaymentsProductionApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Fake_payment_page_is_404_outside_development()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/dev/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_outside_development_still_uses_the_production_gateway_selection()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product($"p-{Guid.NewGuid():N}", "Prod Env Test Product", $"platform-{Guid.NewGuid():N}");
        db.Products.Add(product);
        var variant = new ProductVariant(product.Id, 100m, "ARS");
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/checkout/orders", new
        {
            email = "buyer@example.com",
            items = new[] { new { variantId = variant.Id, quantity = 1 } },
        });

        // AccessToken is empty, so NotConfiguredPaymentGateway (503) must win over the fake.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
