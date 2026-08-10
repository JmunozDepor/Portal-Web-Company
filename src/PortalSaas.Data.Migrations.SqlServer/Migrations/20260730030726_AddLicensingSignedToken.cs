using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddLicensingSignedToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "signed_status_token",
                table: "on_premise_licenses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "signed_status_updated_at",
                table: "on_premise_licenses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "on_premise_license_conflicts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    on_premise_license_id = table.Column<long>(type: "bigint", nullable: false),
                    reported_fingerprint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reported_ip = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    resolved = table.Column<bool>(type: "bit", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    resolved_by_admin_email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_on_premise_license_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "fk_on_premise_license_conflicts_on_premise_licenses_on_premise_license_id",
                        column: x => x.on_premise_license_id,
                        principalTable: "on_premise_licenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_on_premise_license_conflicts_on_premise_license_id",
                table: "on_premise_license_conflicts",
                column: "on_premise_license_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "on_premise_license_conflicts");

            migrationBuilder.DropColumn(
                name: "signed_status_token",
                table: "on_premise_licenses");

            migrationBuilder.DropColumn(
                name: "signed_status_updated_at",
                table: "on_premise_licenses");
        }
    }
}
