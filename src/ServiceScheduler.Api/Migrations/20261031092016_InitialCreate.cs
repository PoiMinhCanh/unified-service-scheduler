using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ServiceScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "Customers",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dealerships",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dealerships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vin = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: false),
                    Make = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.UniqueConstraint("AK_Vehicles_Id_CustomerId", x => new { x.Id, x.CustomerId });
                    table.ForeignKey(
                        name: "FK_Vehicles_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "dbo",
                        principalTable: "Customers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DealershipOpeningHours",
                schema: "dbo",
                columns: table => new
                {
                    DealershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<byte>(type: "tinyint", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DealershipOpeningHours", x => new { x.DealershipId, x.DayOfWeek, x.OpensAt });
                    table.CheckConstraint("CK_DealershipOpeningHours_DayOfWeek", "[DayOfWeek] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_DealershipOpeningHours_Interval", "[ClosesAt] > [OpensAt]");
                    table.ForeignKey(
                        name: "FK_DealershipOpeningHours_Dealerships_DealershipId",
                        column: x => x.DealershipId,
                        principalSchema: "dbo",
                        principalTable: "Dealerships",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ServiceBays",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DealershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceBays", x => x.Id);
                    table.UniqueConstraint("AK_ServiceBays_Id_DealershipId", x => new { x.Id, x.DealershipId });
                    table.ForeignKey(
                        name: "FK_ServiceBays_Dealerships_DealershipId",
                        column: x => x.DealershipId,
                        principalSchema: "dbo",
                        principalTable: "Dealerships",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ServiceTypes",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DealershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTypes", x => x.Id);
                    table.UniqueConstraint("AK_ServiceTypes_Id_DealershipId", x => new { x.Id, x.DealershipId });
                    table.CheckConstraint("CK_ServiceTypes_DurationMinutes", "[DurationMinutes] > 0");
                    table.ForeignKey(
                        name: "FK_ServiceTypes_Dealerships_DealershipId",
                        column: x => x.DealershipId,
                        principalSchema: "dbo",
                        principalTable: "Dealerships",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Technicians",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DealershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Technicians", x => x.Id);
                    table.UniqueConstraint("AK_Technicians_Id_DealershipId", x => new { x.Id, x.DealershipId });
                    table.ForeignKey(
                        name: "FK_Technicians_Dealerships_DealershipId",
                        column: x => x.DealershipId,
                        principalSchema: "dbo",
                        principalTable: "Dealerships",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Appointments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DealershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TechnicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceBayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.CheckConstraint("CK_Appointments_DurationMinutes", "[DurationMinutes] > 0");
                    table.CheckConstraint("CK_Appointments_DurationSnapshot", "[EndUtc] = DATEADD(minute, [DurationMinutes], [StartUtc])");
                    table.CheckConstraint("CK_Appointments_Interval", "[EndUtc] > [StartUtc]");
                    table.CheckConstraint("CK_Appointments_Status", "[Status] IN (N'Confirmed')");
                    table.CheckConstraint("CK_Appointments_UtcOffsets", "DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_Appointments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "dbo",
                        principalTable: "Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Appointments_Dealerships_DealershipId",
                        column: x => x.DealershipId,
                        principalSchema: "dbo",
                        principalTable: "Dealerships",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Appointments_ServiceBays_ServiceBayId_DealershipId",
                        columns: x => new { x.ServiceBayId, x.DealershipId },
                        principalSchema: "dbo",
                        principalTable: "ServiceBays",
                        principalColumns: new[] { "Id", "DealershipId" });
                    table.ForeignKey(
                        name: "FK_Appointments_ServiceTypes_ServiceTypeId_DealershipId",
                        columns: x => new { x.ServiceTypeId, x.DealershipId },
                        principalSchema: "dbo",
                        principalTable: "ServiceTypes",
                        principalColumns: new[] { "Id", "DealershipId" });
                    table.ForeignKey(
                        name: "FK_Appointments_Technicians_TechnicianId_DealershipId",
                        columns: x => new { x.TechnicianId, x.DealershipId },
                        principalSchema: "dbo",
                        principalTable: "Technicians",
                        principalColumns: new[] { "Id", "DealershipId" });
                    table.ForeignKey(
                        name: "FK_Appointments_Vehicles_VehicleId_CustomerId",
                        columns: x => new { x.VehicleId, x.CustomerId },
                        principalSchema: "dbo",
                        principalTable: "Vehicles",
                        principalColumns: new[] { "Id", "CustomerId" });
                });

            migrationBuilder.CreateTable(
                name: "TechnicianQualifications",
                schema: "dbo",
                columns: table => new
                {
                    TechnicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DealershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicianQualifications", x => new { x.TechnicianId, x.ServiceTypeId });
                    table.ForeignKey(
                        name: "FK_TechnicianQualifications_ServiceTypes_ServiceTypeId_DealershipId",
                        columns: x => new { x.ServiceTypeId, x.DealershipId },
                        principalSchema: "dbo",
                        principalTable: "ServiceTypes",
                        principalColumns: new[] { "Id", "DealershipId" });
                    table.ForeignKey(
                        name: "FK_TechnicianQualifications_Technicians_TechnicianId_DealershipId",
                        columns: x => new { x.TechnicianId, x.DealershipId },
                        principalSchema: "dbo",
                        principalTable: "Technicians",
                        principalColumns: new[] { "Id", "DealershipId" });
                });

            migrationBuilder.CreateTable(
                name: "TechnicianShifts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TechnicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicianShifts", x => x.Id);
                    table.CheckConstraint("CK_TechnicianShifts_Interval", "[EndUtc] > [StartUtc]");
                    table.CheckConstraint("CK_TechnicianShifts_UtcOffsets", "DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_TechnicianShifts_Technicians_TechnicianId",
                        column: x => x.TechnicianId,
                        principalSchema: "dbo",
                        principalTable: "Technicians",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Customers",
                columns: new[] { "Id", "Email", "Name" },
                values: new object[,]
                {
                    { new Guid("c1000000-0000-0000-0000-000000000001"), "an.nguyen@example.test", "An Nguyen" },
                    { new Guid("c1000000-0000-0000-0000-000000000002"), "oliver.smith@example.test", "Oliver Smith" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Dealerships",
                columns: new[] { "Id", "Name", "TimeZoneId" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-000000000001"), "Saigon Central Motors", "Asia/Ho_Chi_Minh" },
                    { new Guid("a1000000-0000-0000-0000-000000000002"), "London Riverside Motors", "Europe/London" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "DealershipOpeningHours",
                columns: new[] { "DayOfWeek", "DealershipId", "OpensAt", "ClosesAt" },
                values: new object[,]
                {
                    { (byte)1, new Guid("a1000000-0000-0000-0000-000000000001"), new TimeOnly(9, 0, 0), new TimeOnly(17, 0, 0) },
                    { (byte)2, new Guid("a1000000-0000-0000-0000-000000000001"), new TimeOnly(9, 0, 0), new TimeOnly(17, 0, 0) },
                    { (byte)3, new Guid("a1000000-0000-0000-0000-000000000001"), new TimeOnly(9, 0, 0), new TimeOnly(17, 0, 0) },
                    { (byte)4, new Guid("a1000000-0000-0000-0000-000000000001"), new TimeOnly(9, 0, 0), new TimeOnly(17, 0, 0) },
                    { (byte)5, new Guid("a1000000-0000-0000-0000-000000000001"), new TimeOnly(9, 0, 0), new TimeOnly(17, 0, 0) },
                    { (byte)1, new Guid("a1000000-0000-0000-0000-000000000002"), new TimeOnly(9, 0, 0), new TimeOnly(17, 0, 0) },
                    { (byte)2, new Guid("a1000000-0000-0000-0000-000000000002"), new TimeOnly(9, 0, 0), new TimeOnly(17, 0, 0) },
                    { (byte)3, new Guid("a1000000-0000-0000-0000-000000000002"), new TimeOnly(9, 0, 0), new TimeOnly(17, 0, 0) },
                    { (byte)4, new Guid("a1000000-0000-0000-0000-000000000002"), new TimeOnly(9, 0, 0), new TimeOnly(17, 0, 0) },
                    { (byte)5, new Guid("a1000000-0000-0000-0000-000000000002"), new TimeOnly(9, 0, 0), new TimeOnly(17, 0, 0) }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "ServiceBays",
                columns: new[] { "Id", "DealershipId", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("81000000-0000-0000-0000-000000000001"), new Guid("a1000000-0000-0000-0000-000000000001"), true, "Bay 1" },
                    { new Guid("81000000-0000-0000-0000-000000000002"), new Guid("a1000000-0000-0000-0000-000000000001"), false, "Bay 2" },
                    { new Guid("81000000-0000-0000-0000-000000000003"), new Guid("a1000000-0000-0000-0000-000000000002"), true, "Bay 1" },
                    { new Guid("81000000-0000-0000-0000-000000000004"), new Guid("a1000000-0000-0000-0000-000000000002"), false, "Bay 2" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "ServiceTypes",
                columns: new[] { "Id", "DealershipId", "DurationMinutes", "Name" },
                values: new object[,]
                {
                    { new Guid("51000000-0000-0000-0000-000000000001"), new Guid("a1000000-0000-0000-0000-000000000001"), 60, "Oil Change" },
                    { new Guid("51000000-0000-0000-0000-000000000002"), new Guid("a1000000-0000-0000-0000-000000000001"), 120, "Brake Service" },
                    { new Guid("51000000-0000-0000-0000-000000000003"), new Guid("a1000000-0000-0000-0000-000000000002"), 90, "Annual Service" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Technicians",
                columns: new[] { "Id", "DealershipId", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("71000000-0000-0000-0000-000000000001"), new Guid("a1000000-0000-0000-0000-000000000001"), true, "Minh Tran" },
                    { new Guid("71000000-0000-0000-0000-000000000002"), new Guid("a1000000-0000-0000-0000-000000000001"), false, "Linh Pham" },
                    { new Guid("71000000-0000-0000-0000-000000000003"), new Guid("a1000000-0000-0000-0000-000000000002"), true, "James Wilson" },
                    { new Guid("71000000-0000-0000-0000-000000000004"), new Guid("a1000000-0000-0000-0000-000000000002"), false, "Emma Brown" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Vehicles",
                columns: new[] { "Id", "CustomerId", "Make", "Model", "Vin" },
                values: new object[,]
                {
                    { new Guid("b1000000-0000-0000-0000-000000000001"), new Guid("c1000000-0000-0000-0000-000000000001"), "Honda", "Accord", "1HGCM82633A000001" },
                    { new Guid("b1000000-0000-0000-0000-000000000002"), new Guid("c1000000-0000-0000-0000-000000000002"), "Ford", "Focus", "1HGCM82633A000002" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "TechnicianQualifications",
                columns: new[] { "ServiceTypeId", "TechnicianId", "DealershipId" },
                values: new object[,]
                {
                    { new Guid("51000000-0000-0000-0000-000000000001"), new Guid("71000000-0000-0000-0000-000000000001"), new Guid("a1000000-0000-0000-0000-000000000001") },
                    { new Guid("51000000-0000-0000-0000-000000000002"), new Guid("71000000-0000-0000-0000-000000000001"), new Guid("a1000000-0000-0000-0000-000000000001") },
                    { new Guid("51000000-0000-0000-0000-000000000001"), new Guid("71000000-0000-0000-0000-000000000002"), new Guid("a1000000-0000-0000-0000-000000000001") },
                    { new Guid("51000000-0000-0000-0000-000000000003"), new Guid("71000000-0000-0000-0000-000000000003"), new Guid("a1000000-0000-0000-0000-000000000002") },
                    { new Guid("51000000-0000-0000-0000-000000000003"), new Guid("71000000-0000-0000-0000-000000000004"), new Guid("a1000000-0000-0000-0000-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CustomerId",
                schema: "dbo",
                table: "Appointments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DealershipId_ServiceBayId_StartUtc",
                schema: "dbo",
                table: "Appointments",
                columns: new[] { "DealershipId", "ServiceBayId", "StartUtc" })
                .Annotation("SqlServer:Include", new[] { "EndUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DealershipId_TechnicianId_StartUtc",
                schema: "dbo",
                table: "Appointments",
                columns: new[] { "DealershipId", "TechnicianId", "StartUtc" })
                .Annotation("SqlServer:Include", new[] { "EndUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ServiceTypeId_DealershipId",
                schema: "dbo",
                table: "Appointments",
                columns: new[] { "ServiceTypeId", "DealershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_VehicleId_CustomerId",
                schema: "dbo",
                table: "Appointments",
                columns: new[] { "VehicleId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "UX_Dealerships_Name",
                schema: "dbo",
                table: "Dealerships",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ServiceBays_DealershipId_Name",
                schema: "dbo",
                table: "ServiceBays",
                columns: new[] { "DealershipId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ServiceTypes_DealershipId_Name",
                schema: "dbo",
                table: "ServiceTypes",
                columns: new[] { "DealershipId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TechnicianQualifications_ServiceTypeId_DealershipId",
                schema: "dbo",
                table: "TechnicianQualifications",
                columns: new[] { "ServiceTypeId", "DealershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_Technicians_DealershipId",
                schema: "dbo",
                table: "Technicians",
                column: "DealershipId");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicianShifts_TechnicianId_StartUtc",
                schema: "dbo",
                table: "TechnicianShifts",
                columns: new[] { "TechnicianId", "StartUtc" })
                .Annotation("SqlServer:Include", new[] { "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_TechnicianShifts_TechnicianId_StartUtc_EndUtc",
                schema: "dbo",
                table: "TechnicianShifts",
                columns: new[] { "TechnicianId", "StartUtc", "EndUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_CustomerId",
                schema: "dbo",
                table: "Vehicles",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "UX_Vehicles_Vin",
                schema: "dbo",
                table: "Vehicles",
                column: "Vin",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "DealershipOpeningHours",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TechnicianQualifications",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TechnicianShifts",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ServiceBays",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Vehicles",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ServiceTypes",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Technicians",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Customers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Dealerships",
                schema: "dbo");
        }
    }
}
