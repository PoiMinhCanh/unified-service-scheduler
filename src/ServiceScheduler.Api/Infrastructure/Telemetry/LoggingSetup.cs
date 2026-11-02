using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace ServiceScheduler.Api.Infrastructure.Telemetry;

/// <summary>
/// Configures Serilog as one provider on the normal logging factory. Keeping the factory and the
/// other registered providers intact lets tests capture the same application events using their own
/// ILoggerProvider while container stdout remains the operational log path.
/// </summary>
public static class LoggingSetup
{
    public static void AddSchedulerLogging(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Remove only the framework console/debug/event-source providers so container stdout is
        // Serilog JSON. Test and application providers registered before or after this call remain
        // available to the same ILoggerFactory.
        RemoveFrameworkLoggingProviders(builder.Services);

        builder.Services.AddSerilog((services, configuration) =>
        {
            var environment = services.GetRequiredService<IHostEnvironment>();
            var telemetryOptions = services.GetRequiredService<IOptions<TelemetryOptions>>().Value;

            configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("StackExchange.Redis", LogEventLevel.Warning)
                .Enrich.With(new ActivityLogEnricher())
                .Enrich.With(new ScopePropertyFilter())
                .Enrich.WithProperty("Application", telemetryOptions.ServiceName)
                .Enrich.WithProperty("ApplicationVersion", telemetryOptions.ResolveServiceVersion())
                .Enrich.WithProperty("Environment", environment.EnvironmentName)
                .WriteTo.Console(new CompactJsonFormatter());
        }, preserveStaticLogger: false, writeToProviders: true);
    }

    private static void RemoveFrameworkLoggingProviders(IServiceCollection services)
    {
        var frameworkProviders = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(ILoggerProvider)
                && descriptor.ImplementationType?.Assembly.GetName().Name?.StartsWith(
                    "Microsoft.Extensions.Logging",
                    StringComparison.Ordinal) == true)
            .ToArray();

        foreach (var descriptor in frameworkProviders)
        {
            services.Remove(descriptor);
        }
    }
}
