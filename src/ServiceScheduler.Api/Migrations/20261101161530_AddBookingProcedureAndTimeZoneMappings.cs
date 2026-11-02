using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ServiceScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingProcedureAndTimeZoneMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimeZoneMappings",
                schema: "dbo",
                columns: table => new
                {
                    IanaId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WindowsId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeZoneMappings", x => x.IanaId);
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "TimeZoneMappings",
                columns: new[] { "IanaId", "WindowsId" },
                values: new object[,]
                {
                    { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" },
                    { "Europe/London", "GMT Standard Time" }
                });

            migrationBuilder.CreateIndex(
                name: "UX_TimeZoneMappings_WindowsId",
                schema: "dbo",
                table: "TimeZoneMappings",
                column: "WindowsId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Dealerships_TimeZoneMappings_TimeZoneId",
                schema: "dbo",
                table: "Dealerships",
                column: "TimeZoneId",
                principalSchema: "dbo",
                principalTable: "TimeZoneMappings",
                principalColumn: "IanaId");

            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.CreateAppointment;");

            migrationBuilder.Sql("""
                CREATE PROCEDURE dbo.CreateAppointment
                    @DealershipId uniqueidentifier,
                    @CustomerId uniqueidentifier,
                    @VehicleId uniqueidentifier,
                    @ServiceTypeId uniqueidentifier,
                    @TechnicianId uniqueidentifier,
                    @ServiceBayId uniqueidentifier,
                    @StartUtc datetimeoffset(7),
                    @EvaluatedAtUtc datetimeoffset(7),
                    @AppointmentId uniqueidentifier OUTPUT,
                    @EndUtc datetimeoffset(7) OUTPUT,
                    @DurationMinutes int OUTPUT,
                    @CreatedUtc datetimeoffset(7) OUTPUT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    BEGIN TRY
                        IF @@TRANCOUNT > 0
                            THROW 51003, 'The booking procedure must own its transaction.', 1;

                        IF @DealershipId IS NULL OR @CustomerId IS NULL OR @VehicleId IS NULL
                           OR @ServiceTypeId IS NULL OR @TechnicianId IS NULL OR @ServiceBayId IS NULL
                           OR @StartUtc IS NULL OR @EvaluatedAtUtc IS NULL
                            THROW 51003, 'Required booking inputs are missing.', 1;

                        SET @StartUtc = SWITCHOFFSET(@StartUtc, 0);
                        SET @EvaluatedAtUtc = SWITCHOFFSET(@EvaluatedAtUtc, 0);

                        IF @StartUtc <= @EvaluatedAtUtc
                            THROW 51003, 'The requested start must be in the future.', 1;

                        SET LOCK_TIMEOUT 5000;
                        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
                        BEGIN TRANSACTION;

                        DECLARE @TechnicianActive bit;
                        SELECT @TechnicianActive = technician.IsActive
                        FROM dbo.Technicians AS technician WITH (UPDLOCK, HOLDLOCK)
                        WHERE technician.Id = @TechnicianId
                          AND technician.DealershipId = @DealershipId;

                        IF @TechnicianActive IS NULL OR @TechnicianActive = 0
                            THROW 51004, 'The technician is not active for this dealership.', 1;

                        DECLARE @BayActive bit;
                        SELECT @BayActive = bay.IsActive
                        FROM dbo.ServiceBays AS bay WITH (UPDLOCK, HOLDLOCK)
                        WHERE bay.Id = @ServiceBayId
                          AND bay.DealershipId = @DealershipId;

                        IF @BayActive IS NULL OR @BayActive = 0
                            THROW 51004, 'The service bay is not active for this dealership.', 1;

                        IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @CustomerId)
                            THROW 51003, 'The customer was not found.', 1;

                        IF NOT EXISTS (
                            SELECT 1
                            FROM dbo.Vehicles
                            WHERE Id = @VehicleId
                              AND CustomerId = @CustomerId)
                            THROW 51003, 'The vehicle does not belong to the customer.', 1;

                        DECLARE @ServiceDuration int;
                        SELECT @ServiceDuration = serviceType.DurationMinutes
                        FROM dbo.ServiceTypes AS serviceType
                        WHERE serviceType.Id = @ServiceTypeId
                          AND serviceType.DealershipId = @DealershipId;

                        IF @ServiceDuration IS NULL OR @ServiceDuration <= 0
                            THROW 51003, 'The service type is invalid for this dealership.', 1;

                        SET @DurationMinutes = @ServiceDuration;

                        IF DATEDIFF_BIG(
                                minute,
                                @StartUtc,
                                CONVERT(datetimeoffset(7), '9999-12-31T23:59:59.9999999+00:00')) < @DurationMinutes
                            THROW 51003, 'The service interval is outside the supported range.', 1;

                        SET @EndUtc = DATEADD(minute, @DurationMinutes, @StartUtc);

                        DECLARE @IanaId nvarchar(100), @WindowsId nvarchar(100);
                        SELECT @IanaId = dealership.TimeZoneId
                        FROM dbo.Dealerships AS dealership
                        WHERE dealership.Id = @DealershipId;

                        IF @IanaId IS NULL
                            THROW 51003, 'The dealership was not found.', 1;

                        SELECT @WindowsId = mapping.WindowsId
                        FROM dbo.TimeZoneMappings AS mapping
                        WHERE mapping.IanaId = @IanaId;

                        IF @WindowsId IS NULL
                            THROW 51006, 'No SQL time-zone mapping exists for the dealership.', 1;

                        DECLARE @LocalStart datetimeoffset(7), @LocalEnd datetimeoffset(7);
                        BEGIN TRY
                            SET @LocalStart = @StartUtc AT TIME ZONE @WindowsId;
                            SET @LocalEnd = @EndUtc AT TIME ZONE @WindowsId;
                        END TRY
                        BEGIN CATCH
                            THROW 51006, 'The SQL time-zone mapping is invalid.', 1;
                        END CATCH;

                        IF CAST(@LocalStart AS date) <> CAST(@LocalEnd AS date)
                            THROW 51005, 'The interval must fit within one local opening interval.', 1;

                        DECLARE @LocalStartTime time(7) = CAST(@LocalStart AS time(7));
                        DECLARE @LocalEndTime time(7) = CAST(@LocalEnd AS time(7));
                        DECLARE @LocalDay tinyint = CAST(
                            ((DATEDIFF(day, CONVERT(date, '19000107', 112), CAST(@LocalStart AS date)) % 7) + 7) % 7
                            AS tinyint);

                        IF NOT EXISTS (
                            SELECT 1
                            FROM dbo.DealershipOpeningHours AS openingHour
                            WHERE openingHour.DealershipId = @DealershipId
                              AND openingHour.DayOfWeek = @LocalDay
                              AND openingHour.OpensAt <= @LocalStartTime
                              AND openingHour.ClosesAt >= @LocalEndTime)
                            THROW 51005, 'The interval is outside the dealership opening hours.', 1;

                        IF NOT EXISTS (
                            SELECT 1
                            FROM dbo.TechnicianQualifications AS qualification
                            WHERE qualification.TechnicianId = @TechnicianId
                              AND qualification.ServiceTypeId = @ServiceTypeId
                              AND qualification.DealershipId = @DealershipId)
                            THROW 51004, 'The technician is not qualified for the service.', 1;

                        IF NOT EXISTS (
                            SELECT 1
                            FROM dbo.TechnicianShifts AS shift
                            WHERE shift.TechnicianId = @TechnicianId
                              AND shift.StartUtc <= @StartUtc
                              AND shift.EndUtc >= @EndUtc)
                            THROW 51005, 'The interval is outside the technician shift.', 1;

                        IF EXISTS (
                            SELECT 1
                            FROM dbo.Appointments AS appointment
                            WHERE appointment.DealershipId = @DealershipId
                              AND appointment.TechnicianId = @TechnicianId
                              AND appointment.Status = N'Confirmed'
                              AND appointment.StartUtc < @EndUtc
                              AND appointment.EndUtc > @StartUtc)
                            THROW 51001, 'The technician has a conflicting appointment.', 1;

                        IF EXISTS (
                            SELECT 1
                            FROM dbo.Appointments AS appointment
                            WHERE appointment.DealershipId = @DealershipId
                              AND appointment.ServiceBayId = @ServiceBayId
                              AND appointment.Status = N'Confirmed'
                              AND appointment.StartUtc < @EndUtc
                              AND appointment.EndUtc > @StartUtc)
                            THROW 51002, 'The service bay has a conflicting appointment.', 1;

                        SET @AppointmentId = NEWID();
                        SET @CreatedUtc = CAST(SYSUTCDATETIME() AS datetimeoffset(7));

                        INSERT INTO dbo.Appointments (
                            Id, DealershipId, CustomerId, VehicleId, ServiceTypeId, TechnicianId, ServiceBayId,
                            StartUtc, EndUtc, DurationMinutes, Status, CreatedUtc)
                        VALUES (
                            @AppointmentId, @DealershipId, @CustomerId, @VehicleId, @ServiceTypeId,
                            @TechnicianId, @ServiceBayId, @StartUtc, @EndUtc, @DurationMinutes,
                            N'Confirmed', @CreatedUtc);

                        COMMIT TRANSACTION;
                    END TRY
                    BEGIN CATCH
                        IF XACT_STATE() <> 0
                            ROLLBACK TRANSACTION;

                        SET LOCK_TIMEOUT -1;
                        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
                        SET XACT_ABORT OFF;

                        THROW;
                    END CATCH;

                    SET LOCK_TIMEOUT -1;
                    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
                    SET XACT_ABORT OFF;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.CreateAppointment;");

            migrationBuilder.DropForeignKey(
                name: "FK_Dealerships_TimeZoneMappings_TimeZoneId",
                schema: "dbo",
                table: "Dealerships");

            migrationBuilder.DropTable(
                name: "TimeZoneMappings",
                schema: "dbo");
        }
    }
}
