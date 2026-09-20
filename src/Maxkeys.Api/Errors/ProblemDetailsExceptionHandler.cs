using Maxkeys.Application.Payments;
using Maxkeys.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Maxkeys.Api.Errors;

/// <summary>
/// Central exception-to-Problem-Details mapping (design section 7 error table):
/// <see cref="DomainConflictException"/> → 409, <see cref="DomainException"/> (any
/// other, including the base type) → 422, <see cref="PaymentGatewayException"/> → 503,
/// anything else → 500 with no exception detail in the body. Registered via
/// <c>AddExceptionHandler&lt;ProblemDetailsExceptionHandler&gt;()</c> + <c>UseExceptionHandler()</c>.
/// </summary>
public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    public ProblemDetailsExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ProblemDetailsExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = Map(exception);

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {TraceId}", httpContext.TraceIdentifier);
        }
        else
        {
            _logger.LogWarning(exception, "{Title} for {TraceId}", title, httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = statusCode;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                // Domain messages are authored for the caller (e.g. "Order item already has all
                // required keys assigned."); everything else stays opaque so 500s never leak internals.
                Detail = exception is DomainException ? exception.Message : null,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier },
            },
        });
    }

    /// <summary>Order matters: <see cref="DomainConflictException"/> must be checked before its base <see cref="DomainException"/>.</summary>
    private static (int StatusCode, string Title) Map(Exception exception) => exception switch
    {
        DomainConflictException => (StatusCodes.Status409Conflict, "Conflict"),
        DomainException => (StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity"),
        PaymentGatewayException => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable"),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
    };
}
