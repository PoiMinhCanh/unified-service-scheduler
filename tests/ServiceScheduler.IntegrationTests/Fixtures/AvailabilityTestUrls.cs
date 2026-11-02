namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>Builds encoded availability URLs for the shared API contract.</summary>
public static class AvailabilityTestUrls
{
    public static string For(Guid dealershipId, Guid serviceTypeId, string startsAt) =>
        $"/api/dealerships/{dealershipId}/availability" +
        $"?serviceTypeId={serviceTypeId}&startsAt={Uri.EscapeDataString(startsAt)}";
}
