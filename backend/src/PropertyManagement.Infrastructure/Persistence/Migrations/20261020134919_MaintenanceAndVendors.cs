using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaintenanceAndVendors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RepairApprovalLimit",
                table: "ManagementAgreements",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Trade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                    table.UniqueConstraint("AK_Vendors_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReportedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Urgency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EstimateAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    EstimateNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApprovalLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OwnerApprovalNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OwnerApprovalRecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CompletionNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClosedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantRating = table.Column<int>(type: "int", nullable: true),
                    TenantFeedback = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRequests", x => x.Id);
                    table.UniqueConstraint("AK_MaintenanceRequests_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_MaintenanceRequests_Rating", "[TenantRating] IS NULL OR [TenantRating] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_AspNetUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_AspNetUsers_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_PropertyUnits_OrganizationId_UnitId",
                        columns: x => new { x.OrganizationId, x.UnitId },
                        principalTable: "PropertyUnits",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_Tenants_OrganizationId_TenantId",
                        columns: x => new { x.OrganizationId, x.TenantId },
                        principalTable: "Tenants",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_Vendors_OrganizationId_VendorId",
                        columns: x => new { x.OrganizationId, x.VendorId },
                        principalTable: "Vendors",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceUpdates_AspNetUsers_ByUserId",
                        column: x => x.ByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceUpdates_MaintenanceRequests_OrganizationId_RequestId",
                        columns: x => new { x.OrganizationId, x.RequestId },
                        principalTable: "MaintenanceRequests",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_ApprovedByUserId",
                table: "MaintenanceRequests",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_AssignedUserId_Status",
                table: "MaintenanceRequests",
                columns: new[] { "AssignedUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_CreatedByUserId",
                table: "MaintenanceRequests",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_OrganizationId_Number",
                table: "MaintenanceRequests",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_OrganizationId_PropertyId",
                table: "MaintenanceRequests",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_OrganizationId_Status_Urgency",
                table: "MaintenanceRequests",
                columns: new[] { "OrganizationId", "Status", "Urgency" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_OrganizationId_TenantId",
                table: "MaintenanceRequests",
                columns: new[] { "OrganizationId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_OrganizationId_UnitId",
                table: "MaintenanceRequests",
                columns: new[] { "OrganizationId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_OrganizationId_VendorId",
                table: "MaintenanceRequests",
                columns: new[] { "OrganizationId", "VendorId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_PropertyId_Status",
                table: "MaintenanceRequests",
                columns: new[] { "PropertyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceUpdates_ByUserId",
                table: "MaintenanceUpdates",
                column: "ByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceUpdates_OrganizationId_RequestId",
                table: "MaintenanceUpdates",
                columns: new[] { "OrganizationId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceUpdates_RequestId_Sequence",
                table: "MaintenanceUpdates",
                columns: new[] { "RequestId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_OrganizationId_Name",
                table: "Vendors",
                columns: new[] { "OrganizationId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceUpdates");

            migrationBuilder.DropTable(
                name: "MaintenanceRequests");

            migrationBuilder.DropTable(
                name: "Vendors");

            migrationBuilder.DropColumn(
                name: "RepairApprovalLimit",
                table: "ManagementAgreements");
        }
    }
}
