using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaseNoticesInspectionsReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaseInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InspectedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TenantPresent = table.Column<bool>(type: "bit", nullable: false),
                    TenantAgreed = table.Column<bool>(type: "bit", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InspectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseInspections", x => x.Id);
                    table.UniqueConstraint("AK_LeaseInspections_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaseInspections_AspNetUsers_InspectedByUserId",
                        column: x => x.InspectedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaseInspections_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaseNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ServedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ServedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WithdrawnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaseNotices_AspNetUsers_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaseNotices_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaseReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecipientCount = table.Column<int>(type: "int", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaseReminders_Leases_OrganizationId_LeaseId",
                        columns: x => new { x.OrganizationId, x.LeaseId },
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProposedDeduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionItems_LeaseInspections_OrganizationId_InspectionId",
                        columns: x => new { x.OrganizationId, x.InspectionId },
                        principalTable: "LeaseInspections",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionItems_OrganizationId_InspectionId",
                table: "InspectionItems",
                columns: new[] { "OrganizationId", "InspectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseInspections_InspectedByUserId",
                table: "LeaseInspections",
                column: "InspectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaseInspections_LeaseId_Type",
                table: "LeaseInspections",
                columns: new[] { "LeaseId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaseInspections_OrganizationId_LeaseId",
                table: "LeaseInspections",
                columns: new[] { "OrganizationId", "LeaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseNotices_LeaseId_ServedOn",
                table: "LeaseNotices",
                columns: new[] { "LeaseId", "ServedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseNotices_OrganizationId_LeaseId",
                table: "LeaseNotices",
                columns: new[] { "OrganizationId", "LeaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseNotices_OrganizationId_Status_ExpiresOn",
                table: "LeaseNotices",
                columns: new[] { "OrganizationId", "Status", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseNotices_RecordedByUserId",
                table: "LeaseNotices",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaseReminders_LeaseId_Key",
                table: "LeaseReminders",
                columns: new[] { "LeaseId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaseReminders_OrganizationId_LeaseId",
                table: "LeaseReminders",
                columns: new[] { "OrganizationId", "LeaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionItems");

            migrationBuilder.DropTable(
                name: "LeaseNotices");

            migrationBuilder.DropTable(
                name: "LeaseReminders");

            migrationBuilder.DropTable(
                name: "LeaseInspections");
        }
    }
}
