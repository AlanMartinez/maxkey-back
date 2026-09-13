using Microsoft.Net.Http.Headers;

namespace Maxkeys.Api.Auth;

/// <summary>
/// Rejects a request that presents an <c>Authorization</c> header the JWT
/// bearer handler could not validate, instead of silently falling back to a
/// guest identity (design section 6e: "<c>POST /checkout/orders</c> is
/// anonymous but ... if an <c>Authorization</c> header is present and the
/// principal is not authenticated, respond 401 instead of silently creating a
/// guest order with an expired token"). A request with no header at all still
/// proceeds as a guest (auth spec "Anonymous checkout allowed").
/// </summary>
public sealed class OptionalBearerFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var hasAuthorizationHeader = httpContext.Request.Headers.ContainsKey(HeaderNames.Authorization);
        var isAuthenticated = httpContext.User.Identity?.IsAuthenticated == true;

        if (hasAuthorizationHeader && !isAuthenticated)
        {
            return ValueTask.FromResult<object?>(
                Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized"));
        }

        return next(context);
    }
}
