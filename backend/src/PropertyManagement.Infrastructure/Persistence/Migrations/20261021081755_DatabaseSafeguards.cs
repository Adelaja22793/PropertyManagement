using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database-level safeguards: audit records can't be changed or deleted by any statement, whatever the code does,
    /// and every lease activation writes its unit row so that two racing activations collide.
    /// </summary>
    public partial class DatabaseSafeguards : Migration
    {
        public const string AuditTrigger = "AuditLogs_AppendOnly";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeaseActivationCount",
                table: "PropertyUnits",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql($"""
                CREATE TRIGGER dbo.{AuditTrigger} ON dbo.AuditLogs INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Audit records are append-only.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP TRIGGER dbo.{AuditTrigger};");

            migrationBuilder.DropColumn(
                name: "LeaseActivationCount",
                table: "PropertyUnits");
        }
    }
}
