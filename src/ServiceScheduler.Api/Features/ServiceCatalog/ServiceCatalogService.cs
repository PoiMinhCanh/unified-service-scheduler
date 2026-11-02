using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceScheduler.Api.Features.ServiceCatalog.Contracts;
using ServiceScheduler.Api.Infrastructure.Caching;
using ServiceScheduler.Api.Infrastructure.Persistence;

namespace ServiceScheduler.Api.Features.ServiceCatalog;

/// <summary>Result of a dealership-scoped catalog read.</summary>
public enum ServiceCatalogStatus
{
    Found,
    NotFound,
}

/// <summary>Catalog items for an existing dealership, or a not-found result.</summary>
public sealed record ServiceCatalogResult(ServiceCatalogStatus Status, ServiceTypeResponse[] Services)
{
    public static ServiceCatalogResult Found(ServiceTypeResponse[] services) =>
        new(ServiceCatalogStatus.Found, services);

    public static ServiceCatalogResult NotFound() =>
        new(ServiceCatalogStatus.NotFound, []);
}

/// <summary>
/// Cache-aside catalog read. Hits return the cached payload without SQL Server; misses and
/// recoverable cache failures read SQL Server and attempt a bounded population. Unknown
/// dealerships and dependency failures are never cached.
/// </summary>
public sealed class ServiceCatalogService(
    SchedulerDbContext dbContext,
    BoundedCache cache,
    IOptions<CacheOptions> cacheOptions,
    ILogger<ServiceCatalogService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServiceCatalogResult> GetAsync(Guid dealershipId, CancellationToken cancellationToken)
    {
        var cacheKey = ServiceCatalogCacheKeys.ForDealership(dealershipId);
        var cached = await cache.TryGetAsync(cacheKey, cancellationToken).ConfigureAwait(false);

        if (cached.Status == CacheReadStatus.Hit && cached.Value is not null)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ServiceTypeResponse[]>(cached.Value, JsonOptions);
                if (payload is not null)
                {
                    return ServiceCatalogResult.Found(payload);
                }

                logger.LogError(
                    "EventName {EventName} CacheName {CacheName} CacheResult {CacheResult}; the cached catalog payload deserialized to null and is treated as a miss.",
                    "CachePayloadInvalid",
                    "service_catalog",
                    "failure");
            }
            catch (JsonException exception)
            {
                logger.LogError(
                    exception,
                    "EventName {EventName} CacheName {CacheName} CacheResult {CacheResult}; the cached catalog payload is corrupt, so it is removed and SQL Server is read.",
                    "CacheCorruptPayload",
                    "service_catalog",
                    "failure");
                await cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            }
        }

        var dealershipExists = await dbContext.Dealerships
            .AsNoTracking()
            .AnyAsync(dealership => dealership.Id == dealershipId, cancellationToken)
            .ConfigureAwait(false);

        if (!dealershipExists)
        {
            return ServiceCatalogResult.NotFound();
        }

        var services = await dbContext.ServiceTypes
            .AsNoTracking()
            .Where(serviceType => serviceType.DealershipId == dealershipId)
            .OrderBy(serviceType => serviceType.Name)
            .ThenBy(serviceType => serviceType.Id)
            .Select(serviceType => new ServiceTypeResponse(
                serviceType.Id,
                serviceType.Name,
                serviceType.DurationMinutes))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var serialized = JsonSerializer.SerializeToUtf8Bytes(services, JsonOptions);
        await cache.SetAsync(
            cacheKey,
            serialized,
            cacheOptions.Value.ServiceCatalog.AbsoluteTtl,
            cancellationToken).ConfigureAwait(false);

        return ServiceCatalogResult.Found(services);
    }
}
