using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <summary>Roles an organization defines from the permission list (design 5), covered by the isolation policy.</summary>
    public partial class CustomRoles : Migration
    {
        private static readonly string[] PolicyTables = [.. RowLevelSecurity.Tables, "CustomRoles"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Permissions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomRoles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomRoles_OrganizationId_Name",
                table: "CustomRoles",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.Sql("DROP SECURITY POLICY rls.OrganizationIsolation;");
            migrationBuilder.Sql(RowLevelSecurity.CreatePolicySql(PolicyTables));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY rls.OrganizationIsolation;");
            migrationBuilder.Sql(RowLevelSecurity.CreatePolicySql(RowLevelSecurity.Tables));
            migrationBuilder.DropTable(
                name: "CustomRoles");
        }
    }
}
