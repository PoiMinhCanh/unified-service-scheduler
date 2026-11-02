namespace ServiceScheduler.Api.Infrastructure.Persistence.Entities;

public sealed class DealershipOpeningHour
{
    public Guid DealershipId { get; set; }

    /// <summary>Sunday is 0 and Saturday is 6, independent of the SQL Server DATEFIRST setting.</summary>
    public byte DayOfWeek { get; set; }

    public TimeOnly OpensAt { get; set; }

    public TimeOnly ClosesAt { get; set; }
}
