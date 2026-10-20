using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Documents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SharedWithOwner = table.Column<bool>(type: "bit", nullable: false),
                    SharedWithTenant = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.UniqueConstraint("AK_Documents_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Documents_AspNetUsers_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DeletedByUserId",
                table: "Documents",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrganizationId_ExpiresOn",
                table: "Documents",
                columns: new[] { "OrganizationId", "ExpiresOn" },
                filter: "[ExpiresOn] IS NOT NULL AND [DeletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrganizationId_PropertyId",
                table: "Documents",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrganizationId_Subject_SubjectId",
                table: "Documents",
                columns: new[] { "OrganizationId", "Subject", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_StorageKey",
                table: "Documents",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UploadedByUserId",
                table: "Documents",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Documents");
        }
    }
}
