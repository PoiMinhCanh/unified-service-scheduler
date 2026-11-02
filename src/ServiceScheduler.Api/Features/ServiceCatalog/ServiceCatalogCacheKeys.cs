namespace ServiceScheduler.Api.Features.ServiceCatalog;

/// <summary>
/// Cache key contract for dealership service catalogs. The version segment must be changed when the
/// payload shape or serialization changes.
/// </summary>
public static class ServiceCatalogCacheKeys
{
    public const string Version = "v1";

    public static string ForDealership(Guid dealershipId) =>
        $"scheduler:{Version}:dealership:{dealershipId:D}:service-types";
}
