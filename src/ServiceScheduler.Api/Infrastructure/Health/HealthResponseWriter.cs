using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServiceScheduler.Api.Infrastructure.Health;

/// <summary>
/// Writes check names, statuses, and durations only. Exception messages, endpoints, credentials,
/// and connection strings are never included.
/// </summary>
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";

        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    durationMs = (long)entry.Value.Duration.TotalMilliseconds,
                })
                .ToArray(),
        };

        return context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }
}
