using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AdicionalesManifiesto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manifest_extra_charges",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rate_surcharge_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manifest_extra_charges", x => x.id);
                    table.ForeignKey(
                        name: "FK_manifest_extra_charges_manifests_manifest_id",
                        column: x => x.manifest_id,
                        principalTable: "manifests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_manifest_extra_charges_rate_surcharges",
                        column: x => x.rate_surcharge_id,
                        principalTable: "rate_surcharges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_manifest_extra_charges_company_id",
                table: "manifest_extra_charges",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_manifest_extra_charges_rate_surcharge_id",
                table: "manifest_extra_charges",
                column: "rate_surcharge_id");

            migrationBuilder.CreateIndex(
                name: "uq_manifest_extra_charges_manifest_id_rate_surcharge_id",
                table: "manifest_extra_charges",
                columns: new[] { "manifest_id", "rate_surcharge_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manifest_extra_charges");
        }
    }
}
