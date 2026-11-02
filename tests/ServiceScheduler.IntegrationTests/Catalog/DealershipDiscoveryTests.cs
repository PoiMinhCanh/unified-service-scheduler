using System.Net;
using System.Text.Json;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Catalog;

[Collection(SqlServerCollection.Name)]
public sealed class DealershipDiscoveryTests : SqlServerTestBase
{
    public DealershipDiscoveryTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ReturnsSeededDealershipsOrderedByNameThenId()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/dealerships", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var dealerships = document.RootElement;

        Assert.Equal(2, dealerships.GetArrayLength());
        Assert.Equal("London Riverside Motors", dealerships[0].GetProperty("name").GetString());
        Assert.Equal("Europe/London", dealerships[0].GetProperty("timeZoneId").GetString());
        Assert.Equal("Saigon Central Motors", dealerships[1].GetProperty("name").GetString());
        Assert.Equal("Asia/Ho_Chi_Minh", dealerships[1].GetProperty("timeZoneId").GetString());
        Assert.True(Guid.TryParse(dealerships[0].GetProperty("id").GetString(), out _));
        Assert.True(Guid.TryParse(dealerships[1].GetProperty("id").GetString(), out _));
    }
}
