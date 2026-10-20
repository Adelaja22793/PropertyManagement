using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TwoFactorRequirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorRequiredSinceUtc",
                table: "OrganizationMemberships",
                type: "datetime2",
                nullable: true);

            // Members who already hold such a role start their grace period now.
            migrationBuilder.Sql("""
                UPDATE m SET TwoFactorRequiredSinceUtc = SYSUTCDATETIME()
                FROM OrganizationMemberships m
                WHERE EXISTS (SELECT 1 FROM OrganizationMembershipRoles r WHERE r.MembershipId = m.Id
                              AND r.RoleName IN ('OrganizationAdmin', 'Accountant', 'FinanceApprover'))
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwoFactorRequiredSinceUtc",
                table: "OrganizationMemberships");
        }
    }
}
