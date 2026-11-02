namespace ServiceScheduler.Api.Infrastructure.Caching;

/// <summary>
/// Binds the service-catalog cache budget and the underlying Redis connection limits. The cache
/// connection string is loaded separately from <c>ConnectionStrings:Cache</c> and is never logged.
/// </summary>
public sealed class CacheOptions
{
    public const string SectionName = "Scheduler:Cache";

    public const string ConnectionStringName = "Cache";

    public const string ConnectionStringKey = "ConnectionStrings:Cache";

    public ServiceCatalogCacheOptions ServiceCatalog { get; set; } = new();

    /// <summary>Caller-side budget for each cache get, set, remove, or health operation.</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Timeout for establishing the Redis connection.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Timeout for synchronous and asynchronous Redis commands.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    public int ConnectRetry { get; set; } = 1;

    /// <summary>Set from configuration; validated but never written to logs or health output.</summary>
    public string? ConnectionString { get; set; }
}

/// <summary>Catalog-specific cache settings.</summary>
public sealed class ServiceCatalogCacheOptions
{
    /// <summary>Absolute catalog lifetime; there is no sliding expiration.</summary>
    public TimeSpan AbsoluteTtl { get; set; } = TimeSpan.FromMinutes(10);
}
