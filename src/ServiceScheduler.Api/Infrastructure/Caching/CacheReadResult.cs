namespace ServiceScheduler.Api.Infrastructure.Caching;

/// <summary>Outcome of a bounded cache read.</summary>
public enum CacheReadStatus
{
    /// <summary>A value was returned.</summary>
    Hit,

    /// <summary>The cache was reachable but the key was absent.</summary>
    Miss,

    /// <summary>The cache failed or exceeded its budget; callers fall back to SQL Server.</summary>
    Unavailable,
}

/// <summary>Bounded cache read result. <see cref="Value"/> is set only for a hit.</summary>
public readonly record struct CacheReadResult(CacheReadStatus Status, byte[]? Value);
