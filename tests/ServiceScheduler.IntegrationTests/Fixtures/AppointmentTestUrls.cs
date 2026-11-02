namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>Builds appointment collection and retrieval URLs for the shared API contract.</summary>
public static class AppointmentTestUrls
{
    public static string Collection(Guid dealershipId) =>
        $"/api/dealerships/{dealershipId}/appointments";

    public static string For(Guid dealershipId, Guid appointmentId) =>
        $"{Collection(dealershipId)}/{appointmentId}";
}
