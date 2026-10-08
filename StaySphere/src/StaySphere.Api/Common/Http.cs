using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StaySphere.Application.Abstractions;
using StaySphere.Domain.Common;

namespace StaySphere.Api.Common;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true && UserId is not null;
    public IReadOnlyList<string> Roles => Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => Activity.Current?.TraceId.ToString() ?? accessor.HttpContext?.TraceIdentifier;
    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}

/// <summary>Thin-controller base: map application <see cref="Result"/>s to HTTP responses / RFC 9457 ProblemDetails.</summary>
[ApiController]
[ApiVersion("1.0")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<T> FromResult<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : Problem(result.Error!);

    protected ActionResult<T> Created<T>(Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? base.Created(location(result.Value), result.Value) : Problem(result.Error!);

    protected IActionResult FromResult(Result result) => result.IsSuccess ? NoContent() : Problem(result.Error!);

    protected ObjectResult Problem(Error error) => ErrorMapping.ToProblem(HttpContext, error);
}

public static class ErrorMapping
{
    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.PaymentRequired => StatusCodes.Status402PaymentRequired,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest,
    };

    public static ObjectResult ToProblem(HttpContext context, Error error)
    {
        var status = StatusFor(error.Type);
        var problem = new ProblemDetails
        {
            Type = $"https://staysphere.dev/errors/{error.Code.Replace('.', '-')}",
            Title = error.Message,
            Status = status,
            Instance = context.Request.Path,
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}

/// <summary>Last-resort handler: logs, returns ProblemDetails, never leaks stack traces outside Development.</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, IHostEnvironment env, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away (navigation, closed tab). Not a server error: no 500, no error log.
            logger.LogDebug("Request {Method} {Path} aborted by the client", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = 499;
            return true;
        }

        var (status, title) = exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Authentication is required."),
            BadHttpRequestException bad => (bad.StatusCode, "The request was malformed."),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => (499, "Client closed the request."),
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The resource was modified by someone else. Please retry."),
            TimeoutException => (StatusCodes.Status503ServiceUnavailable, "A dependency timed out. Please retry shortly."),
            _ => (StatusCodes.Status500InternalServerError, "Something went wrong on our side."),
        };
        if (status >= 500) logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://staysphere.dev/errors/http-{status}",
            Detail = env.IsDevelopment() && status >= 500 ? exception.ToString() : null,
        };
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        if (!context.Request.Path.StartsWithSegments("/swagger") && !context.Request.Path.StartsWithSegments("/media"))
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        return next(context);
    }
}
