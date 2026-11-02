using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerCollection.Name)]
public sealed class BookingProcedureTests : SqlServerTestBase
{
    private static readonly DateTimeOffset SlotStartUtc = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public BookingProcedureTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    private BookingProcedureInvoker Invoker => new(RuntimeConnectionString);

    [Fact]
    public async Task ValidCallPersistsAppointmentAndOutputs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await Invoker.ExecuteAsync(Request(), cancellationToken: cancellationToken);

        try
        {
            Assert.NotEqual(Guid.Empty, result.AppointmentId);
            Assert.Equal(SlotStartUtc.AddMinutes(60), result.EndUtc);
            Assert.Equal(60, result.DurationMinutes);
            Assert.Equal(TimeSpan.Zero, result.CreatedUtc.Offset);

            await using var dbContext = CreateDbContext();
            var appointment = await dbContext.Appointments
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == result.AppointmentId, cancellationToken);

            Assert.Equal(SeedData.DealershipIds.Saigon, appointment.DealershipId);
            Assert.Equal(SeedData.CustomerIds.AnNguyen, appointment.CustomerId);
            Assert.Equal(SeedData.VehicleIds.AnNguyenVehicle, appointment.VehicleId);
            Assert.Equal(SeedData.ServiceTypeIds.SaigonOilChange, appointment.ServiceTypeId);
            Assert.Equal(SeedData.TechnicianIds.SaigonActive, appointment.TechnicianId);
            Assert.Equal(SeedData.ServiceBayIds.SaigonActive, appointment.ServiceBayId);
            Assert.Equal(SlotStartUtc, appointment.StartUtc);
            Assert.Equal(SlotStartUtc.AddMinutes(60), appointment.EndUtc);
            Assert.Equal(60, appointment.DurationMinutes);
            Assert.Equal("Confirmed", appointment.Status);
            Assert.Equal(TimeSpan.Zero, appointment.StartUtc.Offset);
            Assert.Equal(TimeSpan.Zero, appointment.EndUtc.Offset);
            Assert.Equal(TimeSpan.Zero, appointment.CreatedUtc.Offset);
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(result.AppointmentId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task TechnicianAndBayConflictsReturnDistinctErrors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var existingId = Guid.NewGuid();
        var secondBayId = Guid.NewGuid();
        ProbeTechnician? secondTechnician = null;

        await TestData.InsertAppointmentAsync(
            existingId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            SeedData.ServiceBayIds.SaigonActive,
            SlotStartUtc,
            SlotStartUtc.AddMinutes(60),
            cancellationToken);

        try
        {
            await TestData.InsertBayAsync(
                secondBayId, SeedData.DealershipIds.Saigon, "Probe Bay", true, cancellationToken);

            await AssertProcedureErrorAsync(
                51001,
                Request(serviceBayId: secondBayId, startUtc: SlotStartUtc.AddMinutes(30)),
                cancellationToken);

            var probe = await InsertProbeTechnicianAsync(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "Probe Technician",
                new DateTimeOffset(2026, 11, 3, 2, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
                cancellationToken);
            secondTechnician = probe;

            await AssertProcedureErrorAsync(
                51002,
                Request(
                    technicianId: probe.TechnicianId,
                    startUtc: SlotStartUtc.AddMinutes(30)),
                cancellationToken);

            Assert.Equal(1, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(existingId, CancellationToken.None);
            if (secondTechnician is not null)
            {
                await DeleteProbeTechnicianAsync(secondTechnician, CancellationToken.None);
            }

            await TestData.DeleteBayAsync(secondBayId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task AdjacentIntervalsSucceedAndOverlapsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var existingId = Guid.NewGuid();
        Guid? adjacentId = null;

        await TestData.InsertAppointmentAsync(
            existingId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            SeedData.ServiceBayIds.SaigonActive,
            SlotStartUtc,
            SlotStartUtc.AddMinutes(60),
            cancellationToken);

        try
        {
            var adjacent = await Invoker.ExecuteAsync(
                Request(startUtc: SlotStartUtc.AddMinutes(60)),
                cancellationToken: cancellationToken);
            adjacentId = adjacent.AppointmentId;
            Assert.Equal(SlotStartUtc.AddMinutes(120), adjacent.EndUtc);

            await AssertProcedureErrorAsync(
                51001,
                Request(startUtc: SlotStartUtc.AddMinutes(30)),
                cancellationToken);
            await AssertProcedureErrorAsync(
                51001,
                Request(startUtc: SlotStartUtc.AddMinutes(15)),
                cancellationToken);
            await AssertProcedureErrorAsync(
                51001,
                Request(startUtc: SlotStartUtc),
                cancellationToken);

            Assert.Equal(2, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            if (adjacentId is not null)
            {
                await TestData.DeleteAppointmentAsync(adjacentId.Value, CancellationToken.None);
            }

            await TestData.DeleteAppointmentAsync(existingId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task DirectCallsEnforceOwnershipScopeAndTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertProcedureErrorAsync(
            51003,
            Request(
                customerId: SeedData.CustomerIds.OliverSmith,
                vehicleId: SeedData.VehicleIds.AnNguyenVehicle),
            cancellationToken);
        await AssertProcedureErrorAsync(
            51003,
            Request(serviceTypeId: SeedData.ServiceTypeIds.LondonAnnualService),
            cancellationToken);
        await AssertProcedureErrorAsync(
            51004,
            Request(technicianId: SeedData.TechnicianIds.LondonActive),
            cancellationToken);
        await AssertProcedureErrorAsync(
            51004,
            Request(serviceBayId: SeedData.ServiceBayIds.LondonActive),
            cancellationToken);
        await AssertProcedureErrorAsync(
            51003,
            Request(startUtc: new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero)),
            cancellationToken);
        await AssertProcedureErrorAsync(
            51003,
            Request(serviceTypeId: Guid.NewGuid()),
            cancellationToken);

        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    [Fact]
    public async Task InactiveAndUnqualifiedResourcesReturn51004()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await TestData.SetTechnicianActiveAsync(
                SeedData.TechnicianIds.SaigonActive, false, cancellationToken);
            await AssertProcedureErrorAsync(51004, Request(), cancellationToken);
        }
        finally
        {
            await TestData.SetTechnicianActiveAsync(
                SeedData.TechnicianIds.SaigonActive, true, CancellationToken.None);
        }

        try
        {
            await TestData.SetBayActiveAsync(SeedData.ServiceBayIds.SaigonActive, false, cancellationToken);
            await AssertProcedureErrorAsync(51004, Request(), cancellationToken);
        }
        finally
        {
            await TestData.SetBayActiveAsync(
                SeedData.ServiceBayIds.SaigonActive, true, CancellationToken.None);
        }

        try
        {
            await TestData.DeleteQualificationAsync(
                SeedData.TechnicianIds.SaigonActive,
                SeedData.ServiceTypeIds.SaigonOilChange,
                cancellationToken);
            await AssertProcedureErrorAsync(51004, Request(), cancellationToken);
        }
        finally
        {
            await TestData.InsertQualificationAsync(
                SeedData.TechnicianIds.SaigonActive,
                SeedData.ServiceTypeIds.SaigonOilChange,
                SeedData.DealershipIds.Saigon,
                CancellationToken.None);
        }

        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    [Fact]
    public async Task OpeningAndShiftCoverageReturn51005AndBoundaryIsAccepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        ProbeTechnician? partialShiftTechnician = null;
        Guid? boundaryAppointmentId = null;

        try
        {
            var probe = await InsertProbeTechnicianAsync(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "Partial Shift Technician",
                new DateTimeOffset(2026, 11, 3, 2, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 3, 3, 30, 0, TimeSpan.Zero),
                cancellationToken);
            partialShiftTechnician = probe;

            await AssertProcedureErrorAsync(
                51005,
                Request(technicianId: probe.TechnicianId),
                cancellationToken);

            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.Saigon, 2, new TimeOnly(9, 0), cancellationToken);
            try
            {
                await AssertProcedureErrorAsync(51005, Request(), cancellationToken);
            }
            finally
            {
                await TestData.InsertOpeningHoursAsync(
                    SeedData.DealershipIds.Saigon,
                    2,
                    new TimeOnly(9, 0),
                    new TimeOnly(17, 0),
                    CancellationToken.None);
            }

            await AssertProcedureErrorAsync(
                51005,
                Request(startUtc: new DateTimeOffset(2026, 11, 3, 16, 30, 0, TimeSpan.Zero)),
                cancellationToken);

            var boundary = await Invoker.ExecuteAsync(
                Request(startUtc: new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero)),
                cancellationToken: cancellationToken);
            boundaryAppointmentId = boundary.AppointmentId;
            Assert.Equal(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero), boundary.EndUtc);

            Assert.Equal(1, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            if (boundaryAppointmentId is not null)
            {
                await TestData.DeleteAppointmentAsync(boundaryAppointmentId.Value, CancellationToken.None);
            }

            if (partialShiftTechnician is not null)
            {
                await DeleteProbeTechnicianAsync(partialShiftTechnician, CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task EquivalentExplicitOffsetsNormalizeToTheSameUtcInstant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await Invoker.ExecuteAsync(
            Request(startUtc: new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(7))),
            cancellationToken: cancellationToken);

        try
        {
            Assert.Equal(SlotStartUtc, result.EndUtc.AddMinutes(-60));
            Assert.Equal(new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero), result.EndUtc);

            await using var dbContext = CreateDbContext();
            var appointment = await dbContext.Appointments
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == result.AppointmentId, cancellationToken);
            Assert.Equal(SlotStartUtc, appointment.StartUtc);
            Assert.Equal(TimeSpan.Zero, appointment.StartUtc.Offset);
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(result.AppointmentId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task CallerOwnedTransactionReturns51003()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertProcedureErrorAsync(51003, Request(), cancellationToken, useOuterTransaction: true);

        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    [Fact]
    public async Task InvalidTimeZoneMappingReturns51006()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await TestData.UpdateTimeZoneMappingWindowsIdAsync(
            "Europe/London", "Not/AZone", cancellationToken);
        try
        {
            await AssertProcedureErrorAsync(
                51006,
                Request(
                    dealershipId: SeedData.DealershipIds.London,
                    serviceTypeId: SeedData.ServiceTypeIds.LondonAnnualService,
                    technicianId: SeedData.TechnicianIds.LondonActive,
                    serviceBayId: SeedData.ServiceBayIds.LondonActive,
                    startUtc: new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero)),
                cancellationToken);
        }
        finally
        {
            await TestData.UpdateTimeZoneMappingWindowsIdAsync(
                "Europe/London", "GMT Standard Time", CancellationToken.None);
        }

        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    [Fact]
    public async Task DirectDstFixturesFollowTheStoredMapping()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var springOpening = new TimeOnly(2, 0);
        var fallOpening = new TimeOnly(0, 0);
        var springShiftId = Guid.NewGuid();
        var fallShiftId = Guid.NewGuid();
        Guid? springAppointmentId = null;
        Guid? fallAppointmentId = null;

        try
        {
            await TestData.InsertOpeningHoursAsync(
                SeedData.DealershipIds.London, 0, springOpening, new TimeOnly(4, 0), cancellationToken);
            await TestData.InsertShiftAsync(
                springShiftId,
                SeedData.TechnicianIds.LondonActive,
                new DateTimeOffset(2027, 3, 28, 1, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 3, 28, 3, 0, 0, TimeSpan.Zero),
                cancellationToken);

            var spring = await Invoker.ExecuteAsync(
                Request(
                    dealershipId: SeedData.DealershipIds.London,
                    serviceTypeId: SeedData.ServiceTypeIds.LondonAnnualService,
                    technicianId: SeedData.TechnicianIds.LondonActive,
                    serviceBayId: SeedData.ServiceBayIds.LondonActive,
                    startUtc: new DateTimeOffset(2027, 3, 28, 1, 30, 0, TimeSpan.Zero)),
                cancellationToken: cancellationToken);
            springAppointmentId = spring.AppointmentId;
            Assert.Equal(new DateTimeOffset(2027, 3, 28, 3, 0, 0, TimeSpan.Zero), spring.EndUtc);

            await TestData.InsertOpeningHoursAsync(
                SeedData.DealershipIds.London, 0, fallOpening, new TimeOnly(4, 0), cancellationToken);
            await TestData.InsertShiftAsync(
                fallShiftId,
                SeedData.TechnicianIds.LondonActive,
                new DateTimeOffset(2027, 10, 31, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 10, 31, 2, 0, 0, TimeSpan.Zero),
                cancellationToken);

            var fall = await Invoker.ExecuteAsync(
                Request(
                    dealershipId: SeedData.DealershipIds.London,
                    serviceTypeId: SeedData.ServiceTypeIds.LondonAnnualService,
                    technicianId: SeedData.TechnicianIds.LondonActive,
                    serviceBayId: SeedData.ServiceBayIds.LondonActive,
                    startUtc: new DateTimeOffset(2027, 10, 31, 0, 30, 0, TimeSpan.Zero)),
                cancellationToken: cancellationToken);
            fallAppointmentId = fall.AppointmentId;
            Assert.Equal(new DateTimeOffset(2027, 10, 31, 2, 0, 0, TimeSpan.Zero), fall.EndUtc);
            await TestData.DeleteAppointmentAsync(fallAppointmentId.Value, CancellationToken.None);
            fallAppointmentId = null;

            await AssertProcedureErrorAsync(
                51005,
                Request(
                    dealershipId: SeedData.DealershipIds.London,
                    serviceTypeId: SeedData.ServiceTypeIds.LondonAnnualService,
                    technicianId: SeedData.TechnicianIds.LondonActive,
                    serviceBayId: SeedData.ServiceBayIds.LondonActive,
                    startUtc: new DateTimeOffset(2027, 10, 31, 3, 0, 0, TimeSpan.Zero)),
                cancellationToken);
        }
        finally
        {
            if (springAppointmentId is not null)
            {
                await TestData.DeleteAppointmentAsync(springAppointmentId.Value, CancellationToken.None);
            }

            if (fallAppointmentId is not null)
            {
                await TestData.DeleteAppointmentAsync(fallAppointmentId.Value, CancellationToken.None);
            }

            await TestData.DeleteShiftAsync(springShiftId, CancellationToken.None);
            await TestData.DeleteShiftAsync(fallShiftId, CancellationToken.None);
            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.London, 0, springOpening, CancellationToken.None);
            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.London, 0, fallOpening, CancellationToken.None);
        }
    }

    private async Task AssertProcedureErrorAsync(
        int expectedNumber,
        BookingProcedureCallRequest request,
        CancellationToken cancellationToken,
        bool useOuterTransaction = false)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(
            () => Invoker.ExecuteAsync(
                request,
                useOuterTransaction: useOuterTransaction,
                cancellationToken: cancellationToken));
        Assert.Equal(expectedNumber, exception.Number);
    }

    private async Task<ProbeTechnician> InsertProbeTechnicianAsync(
        Guid dealershipId,
        Guid serviceTypeId,
        string name,
        DateTimeOffset shiftStartUtc,
        DateTimeOffset shiftEndUtc,
        CancellationToken cancellationToken)
    {
        var technicianId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();

        await TestData.InsertTechnicianAsync(technicianId, dealershipId, name, true, cancellationToken);
        await TestData.InsertQualificationAsync(
            technicianId, serviceTypeId, dealershipId, cancellationToken);
        await TestData.InsertShiftAsync(
            shiftId, technicianId, shiftStartUtc, shiftEndUtc, cancellationToken);

        return new ProbeTechnician(technicianId, shiftId, serviceTypeId);
    }

    private async Task DeleteProbeTechnicianAsync(
        ProbeTechnician probe,
        CancellationToken cancellationToken)
    {
        await TestData.DeleteShiftAsync(probe.ShiftId, cancellationToken);
        await TestData.DeleteQualificationAsync(
            probe.TechnicianId,
            probe.ServiceTypeId,
            cancellationToken);
        await TestData.DeleteTechnicianAsync(probe.TechnicianId, cancellationToken);
    }

    private static BookingProcedureCallRequest Request(
        Guid? dealershipId = null,
        Guid? customerId = null,
        Guid? vehicleId = null,
        Guid? serviceTypeId = null,
        Guid? technicianId = null,
        Guid? serviceBayId = null,
        DateTimeOffset? startUtc = null,
        DateTimeOffset? evaluatedAtUtc = null) =>
        new(
            dealershipId ?? SeedData.DealershipIds.Saigon,
            customerId ?? SeedData.CustomerIds.AnNguyen,
            vehicleId ?? SeedData.VehicleIds.AnNguyenVehicle,
            serviceTypeId ?? SeedData.ServiceTypeIds.SaigonOilChange,
            technicianId ?? SeedData.TechnicianIds.SaigonActive,
            serviceBayId ?? SeedData.ServiceBayIds.SaigonActive,
            startUtc ?? SlotStartUtc,
            evaluatedAtUtc ?? EvaluatedAtUtc);

    private SchedulerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(SetupConnectionString)
            .Options;
        return new SchedulerDbContext(options);
    }

    private sealed record ProbeTechnician(Guid TechnicianId, Guid ShiftId, Guid ServiceTypeId);
}
