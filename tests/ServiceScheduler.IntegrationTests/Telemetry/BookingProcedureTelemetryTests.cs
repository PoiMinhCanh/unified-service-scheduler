using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceScheduler.Api.Features.Appointments;
using ServiceScheduler.Api.Infrastructure.Configuration;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.Api.Infrastructure.Telemetry;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Telemetry;

/// <summary>
/// Exercises the SQL procedure boundary directly so mapped results, unmapped setup failures, and
/// cancellation are observed through the bounded procedure-attempt counter.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BookingProcedureTelemetryTests : SqlServerTestBase
{
    public BookingProcedureTelemetryTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task SuccessfulProcedureRecordsSucceededAndPersistsOneAppointment()
    {
        using var capture = new MeterCapture();
        var procedure = CreateProcedure();
        var request = ValidRequest();
        var before = await TestData.CountAppointmentsAsync(TestContext.Current.CancellationToken);

        var result = await procedure.ExecuteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(BookingProcedureStatus.Succeeded, result.Status);
        Assert.Contains(
            capture.Results,
            outcome => outcome == "succeeded");
        Assert.Equal(before + 1, await TestData.CountAppointmentsAsync(TestContext.Current.CancellationToken));
        await TestData.DeleteAppointmentAsync(result.AppointmentId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MappedProcedureFailureRecordsItsBoundedResult()
    {
        using var capture = new MeterCapture();
        var procedure = CreateProcedure();
        var request = ValidRequest() with
        {
            CustomerId = Guid.NewGuid(),
        };

        var result = await procedure.ExecuteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(BookingProcedureStatus.InvalidRequest, result.Status);
        Assert.Contains(capture.Results, outcome => outcome == "invalid_request");
    }

    [Fact]
    public async Task MissingConnectionStringRecordsUnexpectedAndThrows()
    {
        using var capture = new MeterCapture();
        var procedure = CreateProcedure(runtimeConnectionString: string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => procedure.ExecuteAsync(ValidRequest(), TestContext.Current.CancellationToken));

        Assert.Contains(capture.Results, outcome => outcome == "unexpected");
    }

    [Fact]
    public async Task CancellationIsNotCountedAsAFailedAttempt()
    {
        using var capture = new MeterCapture();
        var procedure = CreateProcedure();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => procedure.ExecuteAsync(ValidRequest(), cancellation.Token));

        Assert.Empty(capture.Results);
    }

    private SqlAppointmentBookingProcedure CreateProcedure(string? runtimeConnectionString = null) =>
        new(
            Options.Create(new DatabaseOptions
            {
                Runtime = runtimeConnectionString ?? RuntimeConnectionString,
            }),
            Options.Create(new BookingOptions()),
            NullLogger<SqlAppointmentBookingProcedure>.Instance);

    private static BookingProcedureRequest ValidRequest() =>
        new(
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            SeedData.ServiceBayIds.SaigonActive,
            new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero),
            SqlServerContainerFixture.FixedClockUtc);

    private sealed class MeterCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<string> _results = [];

        public MeterCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ServiceSchedulerTelemetry.MeterName
                    && instrument.Name == ServiceSchedulerTelemetry.BookingProcedureAttemptsInstrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (string.Equals(tag.Key, "result", StringComparison.Ordinal))
                    {
                        _results.Add(tag.Value?.ToString() ?? string.Empty);
                    }
                }
            });
            _listener.Start();
        }

        public IReadOnlyList<string> Results => _results;

        public void Dispose() => _listener.Dispose();
    }
}
