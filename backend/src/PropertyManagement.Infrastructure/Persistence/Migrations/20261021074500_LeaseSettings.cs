using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <summary>Lease reminder days and organization notice periods (design 8.9), covered by the isolation policy.</summary>
    public partial class LeaseSettings : Migration
    {
        private static readonly string[] PreviousPolicyTables = [.. RowLevelSecurity.Tables, "CustomRoles"];
        private static readonly string[] PolicyTables = [.. PreviousPolicyTables, "LeaseSettings", "NoticePeriodRules"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaseSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffReminderDays = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TenantRemindersEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TenantReminderDays = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaseSettings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NoticePeriodRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Days = table.Column<int>(type: "int", nullable: false),
                    Basis = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticePeriodRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticePeriodRules_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseSettings_OrganizationId",
                table: "LeaseSettings",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoticePeriodRules_OrganizationId_Type_State_Frequency",
                table: "NoticePeriodRules",
                columns: new[] { "OrganizationId", "Type", "State", "Frequency" },
                unique: true);

            migrationBuilder.Sql("DROP SECURITY POLICY rls.OrganizationIsolation;");
            migrationBuilder.Sql(RowLevelSecurity.CreatePolicySql(PolicyTables));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY rls.OrganizationIsolation;");
            migrationBuilder.Sql(RowLevelSecurity.CreatePolicySql(PreviousPolicyTables));

            migrationBuilder.DropTable(
                name: "LeaseSettings");

            migrationBuilder.DropTable(
                name: "NoticePeriodRules");
        }
    }
}
