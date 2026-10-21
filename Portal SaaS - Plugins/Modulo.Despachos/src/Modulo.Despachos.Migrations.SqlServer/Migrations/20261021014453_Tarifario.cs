using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Tarifario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rate_cards",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    carrier_id = table.Column<long>(type: "bigint", nullable: true),
                    shift_id = table.Column<long>(type: "bigint", nullable: true),
                    region_code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    zone_id = table.Column<long>(type: "bigint", nullable: true),
                    commune_id = table.Column<long>(type: "bigint", nullable: true),
                    vehicle_type_id = table.Column<long>(type: "bigint", nullable: true),
                    m3_from = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    m3_to = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    calculation_method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    min_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    parameters_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    priority = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_rate_cards_carriers",
                        column: x => x.carrier_id,
                        principalTable: "carriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rate_cards_communes",
                        column: x => x.commune_id,
                        principalTable: "communes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rate_cards_shifts",
                        column: x => x.shift_id,
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rate_cards_vehicle_types",
                        column: x => x.vehicle_type_id,
                        principalTable: "vehicle_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rate_cards_zones",
                        column: x => x.zone_id,
                        principalTable: "zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rate_surcharges",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    carrier_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    priority = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    condition_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    condition_parameters_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    effect_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_surcharges", x => x.id);
                    table.ForeignKey(
                        name: "fk_rate_surcharges_carriers",
                        column: x => x.carrier_id,
                        principalTable: "carriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_carrier_id",
                table: "rate_cards",
                column: "carrier_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_commune_id",
                table: "rate_cards",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_company_id",
                table: "rate_cards",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_shift_id",
                table: "rate_cards",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_vehicle_type_id",
                table: "rate_cards",
                column: "vehicle_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_zone_id",
                table: "rate_cards",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_surcharges_carrier_id",
                table: "rate_surcharges",
                column: "carrier_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_surcharges_company_id",
                table: "rate_surcharges",
                column: "company_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rate_cards");

            migrationBuilder.DropTable(
                name: "rate_surcharges");
        }
    }
}
