using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Availability;

[Collection(SqlServerCollection.Name)]
public sealed class AvailabilityFailureTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public AvailabilityFailureTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task InvalidTimeZoneDataReturnsSanitizedInternalError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dealershipId = Guid.NewGuid();
        var serviceTypeId = Guid.NewGuid();
        await TestData.InsertTimeZoneMappingAsync(
            "Not/AZone",
            "Tokyo Standard Time",
            cancellationToken);
        await TestData.InsertDealershipAsync(
            dealershipId,
            "Probe Invalid Zone Motors",
            "Not/AZone",
            cancellationToken);
        await TestData.InsertServiceTypeAsync(
            serviceTypeId,
            dealershipId,
            "Probe Invalid Zone Service",
            60,
            cancellationToken);

        try
        {
            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var client = factory.CreateClient();

            using var response = await client.GetAsync(
                AvailabilityTestUrls.For(dealershipId, serviceTypeId, "2026-11-03T03:00:00Z"),
                cancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var problem = JsonDocument.Parse(body);
            Assert.Equal("internal_error", problem.RootElement.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
            Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await TestData.DeleteServiceTypeAsync(serviceTypeId, cancellationToken);
            await TestData.DeleteDealershipAsync(dealershipId, cancellationToken);
            await TestData.DeleteTimeZoneMappingAsync("Not/AZone", cancellationToken);
        }
    }
}
