using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Leases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Leases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousLeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RentAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    BillingFrequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DueDaysBeforePeriod = table.Column<int>(type: "int", nullable: false),
                    GraceDays = table.Column<int>(type: "int", nullable: false),
                    NoticePeriodDays = table.Column<int>(type: "int", nullable: true),
                    HoldoverRentAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RenewalTerms = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActivatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MoveOutDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leases", x => x.Id);
                    table.UniqueConstraint("AK_Leases_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Leases_Dates", "[EndDate] > [StartDate]");
                    table.CheckConstraint("CK_Leases_Rent", "[RentAmount] > 0");
                    table.ForeignKey(
                        name: "FK_Leases_AspNetUsers_ActivatedByUserId",
                        column: x => x.ActivatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leases_Leases_OrganizationId_PreviousLeaseId",
                        columns: x => new { x.OrganizationId, x.PreviousLeaseId },
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leases_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leases_PropertyUnits_OrganizationId_UnitId",
                        columns: x => new { x.OrganizationId, x.UnitId },
                        principalTable: "PropertyUnits",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaseCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Recurrence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseCharges", x => x.Id);
                    table.CheckConstraint("CK_LeaseCharges_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_LeaseCharges_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaseGuarantors",
                columns: table => new
                {
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuarantorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseGuarantors", x => new { x.LeaseId, x.GuarantorId });
                    table.ForeignKey(
                        name: "FK_LeaseGuarantors_Guarantors_OrganizationId_GuarantorId",
                        columns: x => new { x.OrganizationId, x.GuarantorId },
                        principalTable: "Guarantors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaseGuarantors_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaseTenants",
                columns: table => new
                {
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseTenants", x => new { x.LeaseId, x.TenantId });
                    table.ForeignKey(
                        name: "FK_LeaseTenants_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaseTenants_Tenants_OrganizationId_TenantId",
                        columns: x => new { x.OrganizationId, x.TenantId },
                        principalTable: "Tenants",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseCharges_OrganizationId_LeaseId",
                table: "LeaseCharges",
                columns: new[] { "OrganizationId", "LeaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseGuarantors_OrganizationId_GuarantorId",
                table: "LeaseGuarantors",
                columns: new[] { "OrganizationId", "GuarantorId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseGuarantors_OrganizationId_LeaseId",
                table: "LeaseGuarantors",
                columns: new[] { "OrganizationId", "LeaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_ActivatedByUserId",
                table: "Leases",
                column: "ActivatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Leases_OrganizationId_Number",
                table: "Leases",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leases_OrganizationId_PreviousLeaseId",
                table: "Leases",
                columns: new[] { "OrganizationId", "PreviousLeaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_OrganizationId_PropertyId",
                table: "Leases",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_OrganizationId_Status_EndDate",
                table: "Leases",
                columns: new[] { "OrganizationId", "Status", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_OrganizationId_UnitId",
                table: "Leases",
                columns: new[] { "OrganizationId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_PreviousLeaseId",
                table: "Leases",
                column: "PreviousLeaseId",
                unique: true,
                filter: "[PreviousLeaseId] IS NOT NULL AND [Status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_Leases_UnitId_Status",
                table: "Leases",
                columns: new[] { "UnitId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseTenants_OrganizationId_LeaseId",
                table: "LeaseTenants",
                columns: new[] { "OrganizationId", "LeaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseTenants_OrganizationId_TenantId",
                table: "LeaseTenants",
                columns: new[] { "OrganizationId", "TenantId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaseCharges");

            migrationBuilder.DropTable(
                name: "LeaseGuarantors");

            migrationBuilder.DropTable(
                name: "LeaseTenants");

            migrationBuilder.DropTable(
                name: "Leases");
        }
    }
}
