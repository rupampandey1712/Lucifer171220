using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Domain.Platform;

namespace StaySphere.Api.Common;

/// <summary>Runs the FluentValidation validator for every action argument that has one; returns 400 ValidationProblemDetails.</summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values.Where(a => a is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;
            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid) continue;

            var errors = result.Errors.GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            var problem = new ValidationProblemDetails(errors)
            {
                Title = "One or more fields are invalid.",
                Status = StatusCodes.Status400BadRequest,
                Type = "https://staysphere.dev/errors/validation",
            };
            problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
            context.Result = new BadRequestObjectResult(problem);
            return;
        }

        await next();
    }
}

/// <summary>
/// Idempotency-Key support (Stripe-style). The first request with a key executes and its response is stored; retries
/// with the same key and same body replay the stored response; a different body is rejected (422); a concurrent
/// duplicate gets 409. Records expire after 24 hours.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute(string scope, bool required = true) : Attribute, IFilterFactory
{
    public const string HeaderName = "Idempotency-Key";

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        new IdempotencyFilter(scope, required, serviceProvider.GetRequiredService<IAppDbContext>(),
            serviceProvider.GetRequiredService<ICurrentUser>(), serviceProvider.GetRequiredService<TimeProvider>());
}

internal sealed class IdempotencyFilter(string scope, bool required, IAppDbContext db, ICurrentUser user, TimeProvider clock) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var key = context.HttpContext.Request.Headers[IdempotentAttribute.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            if (required)
                context.Result = Problem(context, StatusCodes.Status400BadRequest, "idempotency.missing_key", "An Idempotency-Key header is required for this request.");
            else
                await next();
            return;
        }

        if (key.Length > 100 || user.UserId is not { } userId)
        {
            context.Result = Problem(context, StatusCodes.Status400BadRequest, "idempotency.invalid_key", "Invalid Idempotency-Key.");
            return;
        }

        var fullScope = $"{scope}:{context.HttpContext.Request.Path}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            context.ActionArguments.Where(a => a.Value is not CancellationToken).ToDictionary(a => a.Key, a => a.Value), Json.Options))));

        var existing = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Scope == fullScope && r.UserId == userId && r.Key == key, context.HttpContext.RequestAborted);
        if (existing is null)
        {
            var record = IdempotencyRecord.Begin(fullScope, userId, key, hash, clock.GetUtcNow());
            db.IdempotencyRecords.Add(record);
            try
            {
                await db.SaveChangesAsync(context.HttpContext.RequestAborted);
                db.ResetTracking();
            }
            catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
            {
                db.ResetTracking();
                context.Result = Problem(context, StatusCodes.Status409Conflict, "idempotency.in_progress", "A request with this Idempotency-Key is already being processed.");
                return;
            }
        }
        else
        {
            context.Result = existing switch
            {
                { RequestHash: var h } when h != hash =>
                    Problem(context, StatusCodes.Status422UnprocessableEntity, "idempotency.key_reused", "This Idempotency-Key was already used with a different request."),
                { IsCompleted: false } =>
                    Problem(context, StatusCodes.Status409Conflict, "idempotency.in_progress", "A request with this Idempotency-Key is already being processed."),
                _ => Replay(context, existing),
            };
            return;
        }

        var executed = await next();
        var (status, body) = executed.Result switch
        {
            ObjectResult o => (o.StatusCode ?? StatusCodes.Status200OK, JsonSerializer.Serialize(o.Value, o.Value?.GetType() ?? typeof(object), Json.Options)),
            StatusCodeResult s => (s.StatusCode, (string?)null),
            _ => (executed.Exception is null ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError, (string?)null),
        };

        if (status >= 500 || executed.Exception is not null)
        {
            // Let the client retry a server failure with the same key.
            await db.IdempotencyRecords.Where(r => r.Scope == fullScope && r.UserId == userId && r.Key == key).ExecuteDeleteAsync(CancellationToken.None);
            return;
        }

        await db.IdempotencyRecords.Where(r => r.Scope == fullScope && r.UserId == userId && r.Key == key)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ResponseStatus, status).SetProperty(r => r.ResponseBody, body), CancellationToken.None);
    }

    private static ContentResult Replay(ActionExecutingContext context, IdempotencyRecord record)
    {
        context.HttpContext.Response.Headers["Idempotent-Replayed"] = "true";
        return new ContentResult
        {
            StatusCode = record.ResponseStatus,
            Content = record.ResponseBody ?? string.Empty,
            ContentType = record.ResponseStatus >= 400 ? "application/problem+json" : "application/json",
        };
    }

    private static ObjectResult Problem(ActionExecutingContext context, int status, string code, string title)
    {
        var result = ErrorMapping.ToProblem(context.HttpContext, new Domain.Common.Error(code, title));
        result.StatusCode = status;
        ((ProblemDetails)result.Value!).Status = status;
        return result;
    }
}
