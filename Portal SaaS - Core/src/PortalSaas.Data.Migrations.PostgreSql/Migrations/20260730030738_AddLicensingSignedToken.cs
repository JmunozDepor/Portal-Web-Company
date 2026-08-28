using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
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
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "signed_status_updated_at",
                table: "on_premise_licenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "on_premise_license_conflicts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    on_premise_license_id = table.Column<long>(type: "bigint", nullable: false),
                    reported_fingerprint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reported_ip = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_admin_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_on_premise_license_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "fk_on_premise_license_conflicts_on_premise_licenses_on_premise",
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
