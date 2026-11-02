using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ServiceScheduler.Api.Infrastructure.OpenApi;

/// <summary>Documents the stable <c>code</c> and <c>traceId</c> Problem Details extensions.</summary>
public sealed class ProblemDetailsSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (context.JsonTypeInfo?.Type == typeof(ProblemDetails) && schema.Properties is not null)
        {
            schema.Properties["code"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "Stable application error code: validation_error, not_found, capacity_unavailable, dependency_unavailable, or internal_error.",
            };
            schema.Properties["traceId"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "W3C trace ID (32 lowercase hex) when telemetry is active; use it to find the request trace in Tempo. When no activity exists, a non-empty request-identifier fallback is returned. Logs carry the same TraceId and a SpanId.",
            };
        }

        return Task.CompletedTask;
    }
}
