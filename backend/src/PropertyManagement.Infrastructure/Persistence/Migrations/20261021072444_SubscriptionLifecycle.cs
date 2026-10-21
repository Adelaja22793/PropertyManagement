using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "OrganizationSubscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastNoticeKey",
                table: "OrganizationSubscriptions",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PastDueSinceUtc",
                table: "OrganizationSubscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAtUtc",
                table: "OrganizationSubscriptions",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "LastNoticeKey",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "PastDueSinceUtc",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "SuspendedAtUtc",
                table: "OrganizationSubscriptions");
        }
    }
}
