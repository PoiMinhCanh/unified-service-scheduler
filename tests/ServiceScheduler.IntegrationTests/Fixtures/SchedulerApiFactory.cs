using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>
/// Hosts the API for tests. The default runtime connection is syntactically valid but points at a
/// non-listening endpoint, so no SQL Server is required for startup, health, OpenAPI, or 404 checks.
/// The default cache connection is likewise valid but unreachable.
/// </summary>
public sealed class SchedulerApiFactory : WebApplicationFactory<Program>
{
    public const string UnreachableRuntimeConnection =
        "Server=127.0.0.1,1;Database=ServiceScheduler;User Id=scheduler_app;Password=TestOnly1!;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1";

    public const string UnreachableCacheConnection = "127.0.0.1:1";

    private static readonly IReadOnlyDictionary<string, string?> DefaultSettings =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Runtime"] = UnreachableRuntimeConnection,
            ["ConnectionStrings:Cache"] = UnreachableCacheConnection,
        };

    private readonly IReadOnlyDictionary<string, string?> _settings;

    public SchedulerApiFactory()
        : this(new Dictionary<string, string?>())
    {
    }

    public SchedulerApiFactory(IReadOnlyDictionary<string, string?> settings)
    {
        var merged = new Dictionary<string, string?>(DefaultSettings, StringComparer.Ordinal);
        foreach (var setting in settings)
        {
            merged[setting.Key] = setting.Value;
        }

        _settings = merged;
    }

    /// <summary>Replaces the application clock for deterministic availability tests.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Additional service overrides, for example a capturing logger provider.</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(_settings);
        });
        builder.ConfigureTestServices(services =>
        {
            if (TimeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(TimeProvider);
            }

            ConfigureTestServices?.Invoke(services);
        });
    }
}
