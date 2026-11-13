using LedgerCore.Api.Controllers;
using LedgerCore.Application.Abstractions.Idempotency;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LedgerCore.Api.OpenApi;

/// <summary>
/// Shows the Idempotency-Key header on every write endpoint in Swagger UI, so it can be filled in
/// there. Login is the one POST without it: it changes no state.
/// </summary>
internal sealed class IdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var isLogin = context.MethodInfo.DeclaringType == typeof(AuthController)
            && context.MethodInfo.Name == nameof(AuthController.Login);

        if (!HttpMethods.IsPost(context.ApiDescription.HttpMethod ?? string.Empty) || isLogin)
        {
            return;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = IIdempotencyContext.HeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Description = $"Any unique value, at most {IIdempotencyContext.MaxKeyLength} characters, e.g. a new GUID. "
                + "Retrying with the same key returns the first response instead of running the request again.",
            Schema = new OpenApiSchema { Type = "string", MaxLength = IIdempotencyContext.MaxKeyLength },
        });
    }
}
