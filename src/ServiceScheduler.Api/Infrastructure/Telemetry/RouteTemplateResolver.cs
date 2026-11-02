using System.Text;
using Microsoft.AspNetCore.Routing.Patterns;

namespace ServiceScheduler.Api.Infrastructure.Telemetry;

/// <summary>
/// Resolves the bounded route template used in request telemetry. MVC combines attribute and
/// conventional patterns, which can leave <c>RoutePattern.RawText</c> null; the pattern is
/// reconstructed from its segments so the log/metric label remains the template, not the raw path.
/// </summary>
public static class RouteTemplateResolver
{
    public const string Unmatched = "unmatched";

    public const string HttpContextItemKey = "scheduler.route.template";

    /// <summary>
    /// Resolves the route once per request and caches it so later error handlers use the same
    /// bounded template even if the endpoint is no longer available in the current context.
    /// </summary>
    public static string GetOrStore(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(HttpContextItemKey, out var value)
            && value is string cached)
        {
            return cached;
        }

        var routeTemplate = Resolve(context);
        context.Items[HttpContextItemKey] = routeTemplate;
        return routeTemplate;
    }

    public static string Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.GetEndpoint() is not RouteEndpoint routeEndpoint)
        {
            return Unmatched;
        }

        var pattern = routeEndpoint.RoutePattern;
        return pattern.RawText is { Length: > 0 } rawText
            ? rawText
            : Render(pattern);
    }

    private static string Render(RoutePattern pattern)
    {
        if (pattern.PathSegments.Count == 0)
        {
            return "/";
        }

        var builder = new StringBuilder();
        for (var segmentIndex = 0; segmentIndex < pattern.PathSegments.Count; segmentIndex++)
        {
            if (segmentIndex > 0)
            {
                builder.Append('/');
            }

            foreach (var part in pattern.PathSegments[segmentIndex].Parts)
            {
                switch (part)
                {
                    case RoutePatternLiteralPart literal:
                        builder.Append(literal.Content);
                        break;
                    case RoutePatternSeparatorPart separator:
                        builder.Append(separator.Content);
                        break;
                    case RoutePatternParameterPart parameter:
                        builder.Append('{').Append(parameter.Name).Append('}');
                        break;
                    default:
                        return Unmatched;
                }
            }
        }

        return builder.Length == 0 ? Unmatched : builder.ToString();
    }
}
