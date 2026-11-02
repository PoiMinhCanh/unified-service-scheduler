using Serilog.Core;
using Serilog.Events;

namespace ServiceScheduler.Api.Infrastructure.Telemetry;

/// <summary>
/// Removes ASP.NET Core scope values (raw request path, request/connection identifiers) that the
/// Microsoft logging pipeline copies into every provider's state. The bounded route template and
/// the W3C trace/span fields are emitted explicitly and remain.
/// </summary>
public sealed class ScopePropertyFilter : ILogEventEnricher
{
    private static readonly string[] FrameworkScopeProperties =
    [
        "RequestPath",
        "RequestId",
        "ConnectionId",
        "ActionId",
        "ActionName",
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (var propertyName in FrameworkScopeProperties)
        {
            logEvent.RemovePropertyIfPresent(propertyName);
        }
    }
}
