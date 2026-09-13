using Maxkeys.Api.Endpoints;

namespace Maxkeys.Api.Tests.Checkout;

/// <summary>
/// Compile-time-adjacent guard for task 9.6: the checkout HTTP body model must
/// never gain a <c>UserId</c> member. <c>UserId</c> is derived only from the
/// optional bearer <c>sub</c> claim (design section 6a/6e), never from the
/// request body — regardless of what a caller sends in the JSON payload.
/// </summary>
public sealed class CheckoutOrderRequestBodyTests
{
    [Fact]
    public void Request_body_has_no_UserId_member()
    {
        var property = typeof(CheckoutOrderRequestBody).GetProperty("UserId");

        Assert.Null(property);
    }
}
