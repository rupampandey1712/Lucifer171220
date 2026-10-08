using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace StaySphere.Api.Common;

/// <summary>
/// Documents the success payload of every ActionResult&lt;T&gt; endpoint (ApiExplorer stops inferring it once error
/// responses are declared), so the generated TypeScript client gets fully typed responses.
/// </summary>
public sealed class SuccessResponseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Responses ??= new OpenApiResponses();
        if (operation.Responses.Keys.Any(k => k.StartsWith('2'))) return;

        var type = context.MethodInfo.ReturnType;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)) type = type.GetGenericArguments()[0];
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ActionResult<>))
        {
            operation.Responses["204"] = new OpenApiResponse { Description = "No Content" };
            return;
        }

        var payload = type.GetGenericArguments()[0];
        var schema = context.SchemaGenerator.GenerateSchema(payload, context.SchemaRepository);
        var status = context.MethodInfo.Name == "Create" ? "201" : "200";
        operation.Responses[status] = new OpenApiResponse
        {
            Description = status == "201" ? "Created" : "OK",
            Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = schema } },
        };
    }
}
