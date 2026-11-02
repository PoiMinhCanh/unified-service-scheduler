using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerCollection.Name)]
public sealed class BookingFailureBoundaryTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartsAtUtc = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);
    private const string StartsAt = "2026-11-03T03:00:00Z";

    public BookingFailureBoundaryTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task HeldTechnicianLockProducesLockTimeoutAndReleasesResources()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var invoker = new BookingProcedureInvoker(RuntimeConnectionString);

        await using var holdConnection = new SqlConnection(SetupConnectionString);
        await holdConnection.OpenAsync(cancellationToken);
        await using var holdTransaction = (SqlTransaction)await holdConnection.BeginTransactionAsync(cancellationToken);
        await HoldTechnicianRowAsync(holdConnection, holdTransaction, cancellationToken);

        try
        {
            var exception = await Assert.ThrowsAsync<SqlException>(
                () => invoker.ExecuteAsync(Request(), cancellationToken: cancellationToken));
            Assert.Equal(1222, exception.Number);
            Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            await holdTransaction.RollbackAsync(CancellationToken.None);
        }

        var valid = await invoker.ExecuteAsync(Request(), cancellationToken: cancellationToken);
        try
        {
            Assert.NotEqual(Guid.Empty, valid.AppointmentId);
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(valid.AppointmentId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task HeldTechnicianLockProducesCommandTimeoutWithoutRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var invoker = new BookingProcedureInvoker(RuntimeConnectionString);

        await using var holdConnection = new SqlConnection(SetupConnectionString);
        await holdConnection.OpenAsync(cancellationToken);
        await using var holdTransaction = (SqlTransaction)await holdConnection.BeginTransactionAsync(cancellationToken);
        await HoldTechnicianRowAsync(holdConnection, holdTransaction, cancellationToken);

        try
        {
            var exception = await Assert.ThrowsAsync<SqlException>(
                () => invoker.ExecuteAsync(Request(), commandTimeoutSeconds: 1, cancellationToken: cancellationToken));
            Assert.Equal(-2, exception.Number);
            Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            await holdTransaction.RollbackAsync(CancellationToken.None);
        }

        var valid = await invoker.ExecuteAsync(Request(), cancellationToken: cancellationToken);
        try
        {
            Assert.NotEqual(Guid.Empty, valid.AppointmentId);
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(valid.AppointmentId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task CancellationDuringALockWaitReleasesResources()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var invoker = new BookingProcedureInvoker(RuntimeConnectionString);

        await using var holdConnection = new SqlConnection(SetupConnectionString);
        await holdConnection.OpenAsync(cancellationToken);
        await using var holdTransaction = (SqlTransaction)await holdConnection.BeginTransactionAsync(cancellationToken);
        await HoldTechnicianRowAsync(holdConnection, holdTransaction, cancellationToken);

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            var request = invoker.ExecuteAsync(Request(), cancellationToken: cancellationTokenSource.Token);
            await Task.Delay(200, cancellationToken);
            cancellationTokenSource.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            await holdTransaction.RollbackAsync(CancellationToken.None);
        }

        var valid = await invoker.ExecuteAsync(Request(), cancellationToken: cancellationToken);
        try
        {
            Assert.NotEqual(Guid.Empty, valid.AppointmentId);
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(valid.AppointmentId, CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(BookingProcedureStatus.LockTimeout)]
    [InlineData(BookingProcedureStatus.CommandTimeout)]
    [InlineData(BookingProcedureStatus.DependencyUnavailable)]
    public async Task DependencyAndTimeoutResultsMapTo503WithoutReplay(BookingProcedureStatus status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fake = new FakeAppointmentBookingProcedure(status);
        using var factory = CreateFactory(
            new FakeTimeProvider(FixedClockUtc),
            services => ReplaceProcedure(services, fake));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
            AppointmentRequests.ForSaigon(StartsAt),
            cancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.ServiceUnavailable, "dependency_unavailable", cancellationToken);
        Assert.Equal(1, fake.CallCount);
        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    [Fact]
    public async Task ExhaustedDeadlocksMapTo503AfterBoundedRetries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fake = new FakeAppointmentBookingProcedure(
            BookingProcedureStatus.Deadlock,
            BookingProcedureStatus.Deadlock,
            BookingProcedureStatus.Deadlock);
        using var factory = CreateFactory(
            new FakeTimeProvider(FixedClockUtc),
            services => ReplaceProcedure(services, fake),
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Scheduler:Booking:RetryDelayMilliseconds"] = "0",
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
            AppointmentRequests.ForSaigon(StartsAt),
            cancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.ServiceUnavailable, "dependency_unavailable", cancellationToken);
        Assert.Equal(3, fake.CallCount);
        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    [Fact]
    public async Task InvalidTimeZoneMappingMapsToSanitized500()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fake = new FakeAppointmentBookingProcedure(BookingProcedureStatus.TimeZoneMappingInvalid);
        using var factory = CreateFactory(
            new FakeTimeProvider(FixedClockUtc),
            services => ReplaceProcedure(services, fake));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
            AppointmentRequests.ForSaigon(StartsAt),
            cancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.InternalServerError, "internal_error", cancellationToken);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task UnexpectedExceptionMapsToSanitized500()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fake = new FakeAppointmentBookingProcedure
        {
            Handler = static (_, _) => Task.FromException<BookingProcedureResult>(
                new InvalidOperationException("unexpected")),
        };
        using var factory = CreateFactory(
            new FakeTimeProvider(FixedClockUtc),
            services => ReplaceProcedure(services, fake));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
            AppointmentRequests.ForSaigon(StartsAt),
            cancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.InternalServerError, "internal_error", cancellationToken);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task BudgetExpiryMapsTo503AndStopsBeforeTheNextWrite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(FixedClockUtc);
        var fake = new FakeAppointmentBookingProcedure
        {
            Handler = (_, _) =>
            {
                clock.Advance(TimeSpan.FromSeconds(3));
                return Task.FromResult(new BookingProcedureResult(BookingProcedureStatus.Deadlock));
            },
        };
        using var factory = CreateFactory(
            clock,
            services => ReplaceProcedure(services, fake),
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Scheduler:Booking:RequestBudgetSeconds"] = "2",
                ["Scheduler:Booking:CommandTimeoutSeconds"] = "1",
                ["Scheduler:Booking:RetryDelayMilliseconds"] = "0",
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
            AppointmentRequests.ForSaigon(StartsAt),
            cancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.ServiceUnavailable, "dependency_unavailable", cancellationToken);
        Assert.Equal(1, fake.CallCount);
    }

    private static void ReplaceProcedure(
        IServiceCollection services,
        FakeAppointmentBookingProcedure fake)
    {
        services.RemoveAll<IAppointmentBookingProcedure>();
        services.AddSingleton<IAppointmentBookingProcedure>(fake);
    }

    private static async Task HoldTechnicianRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT [Id]
            FROM dbo.Technicians WITH (UPDLOCK, HOLDLOCK)
            WHERE [Id] = @Id;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = SeedData.TechnicianIds.SaigonActive;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        Assert.Equal(SeedData.TechnicianIds.SaigonActive, (Guid)result!);
    }

    private static BookingProcedureCallRequest Request() =>
        new(
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            SeedData.ServiceBayIds.SaigonActive,
            StartsAtUtc,
            FixedClockUtc);

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        CancellationToken cancellationToken)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(
            "StackTrace",
            document.RootElement.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Password",
            document.RootElement.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
