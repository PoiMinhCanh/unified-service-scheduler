using System.Diagnostics;

namespace ServiceScheduler.Api.Infrastructure.Telemetry;

/// <summary>
/// Resolves the Problem Details trace identifier. When an Activity exists the value is the W3C
/// trace ID (32 lowercase hex) shared with Serilog and Tempo; otherwise the request identifier is a
/// non-empty fallback for telemetry-disabled and startup cases.
/// </summary>
public static class ProblemTraceId
{
    public static string? FromCurrentActivity() => Activity.Current?.TraceId.ToHexString();

    public static string Resolve(string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        return FromCurrentActivity() ?? fallback;
    }
}
