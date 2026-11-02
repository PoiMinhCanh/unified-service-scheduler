namespace ServiceScheduler.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Maps the canonical IANA time-zone identifier persisted on a dealership to the Windows
/// identifier accepted by SQL Server's <c>AT TIME ZONE</c> clause.
/// </summary>
public sealed class TimeZoneMapping
{
    public string IanaId { get; set; } = string.Empty;

    public string WindowsId { get; set; } = string.Empty;
}
