using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carriers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    tax_id = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carriers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "communes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    cut_code = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    region_code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    region_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holidays", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_recipients",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_recipients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rejection_reasons",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    applies_to = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rejection_reasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shifts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    capacity_m3 = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    capacity_kg = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "zones",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drivers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tax_id = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    carrier_id = table.Column<long>(type: "bigint", nullable: true),
                    license_class = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    license_expires_at = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drivers", x => x.id);
                    table.ForeignKey(
                        name: "fk_drivers_carriers",
                        column: x => x.carrier_id,
                        principalTable: "carriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "origins",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: true),
                    commune_id = table.Column<long>(type: "bigint", nullable: true),
                    sap_warehouse_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_origins", x => x.id);
                    table.ForeignKey(
                        name: "fk_origins_communes",
                        column: x => x.commune_id,
                        principalTable: "communes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    plate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    vehicle_type_id = table.Column<long>(type: "bigint", nullable: true),
                    carrier_id = table.Column<long>(type: "bigint", nullable: true),
                    capacity_m3 = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    is_trailer = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicles", x => x.id);
                    table.ForeignKey(
                        name: "fk_vehicles_carriers",
                        column: x => x.carrier_id,
                        principalTable: "carriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vehicles_vehicle_types",
                        column: x => x.vehicle_type_id,
                        principalTable: "vehicle_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "zone_communes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    zone_id = table.Column<long>(type: "bigint", nullable: false),
                    commune_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zone_communes", x => x.id);
                    table.ForeignKey(
                        name: "fk_zone_communes_communes",
                        column: x => x.commune_id,
                        principalTable: "communes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zone_communes_zones",
                        column: x => x.zone_id,
                        principalTable: "zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_carriers_company_id",
                table: "carriers",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_carriers_company_id_code",
                table: "carriers",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_communes_company_id",
                table: "communes",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_communes_company_id_cut_code",
                table: "communes",
                columns: new[] { "company_id", "cut_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_drivers_carrier_id",
                table: "drivers",
                column: "carrier_id");

            migrationBuilder.CreateIndex(
                name: "ix_drivers_company_id",
                table: "drivers",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_drivers_company_id_tax_id",
                table: "drivers",
                columns: new[] { "company_id", "tax_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holidays_company_id",
                table: "holidays",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_holidays_company_id_date",
                table: "holidays",
                columns: new[] { "company_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_recipients_company_id",
                table: "notification_recipients",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_notification_recipients_company_id_user_id",
                table: "notification_recipients",
                columns: new[] { "company_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_origins_commune_id",
                table: "origins",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_origins_company_id",
                table: "origins",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_origins_company_id_code",
                table: "origins",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rejection_reasons_company_id",
                table: "rejection_reasons",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_rejection_reasons_company_id_code",
                table: "rejection_reasons",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shifts_company_id",
                table: "shifts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_shifts_company_id_code",
                table: "shifts",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_types_company_id",
                table: "vehicle_types",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_vehicle_types_company_id_code",
                table: "vehicle_types",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_carrier_id",
                table: "vehicles",
                column: "carrier_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_company_id",
                table: "vehicles",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_vehicle_type_id",
                table: "vehicles",
                column: "vehicle_type_id");

            migrationBuilder.CreateIndex(
                name: "uq_vehicles_company_id_plate",
                table: "vehicles",
                columns: new[] { "company_id", "plate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_zone_communes_commune_id",
                table: "zone_communes",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_zone_communes_company_id",
                table: "zone_communes",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_zone_communes_zone_id_commune_id",
                table: "zone_communes",
                columns: new[] { "zone_id", "commune_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_zones_company_id",
                table: "zones",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_zones_company_id_code",
                table: "zones",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drivers");

            migrationBuilder.DropTable(
                name: "holidays");

            migrationBuilder.DropTable(
                name: "notification_recipients");

            migrationBuilder.DropTable(
                name: "origins");

            migrationBuilder.DropTable(
                name: "rejection_reasons");

            migrationBuilder.DropTable(
                name: "shifts");

            migrationBuilder.DropTable(
                name: "vehicles");

            migrationBuilder.DropTable(
                name: "zone_communes");

            migrationBuilder.DropTable(
                name: "carriers");

            migrationBuilder.DropTable(
                name: "vehicle_types");

            migrationBuilder.DropTable(
                name: "communes");

            migrationBuilder.DropTable(
                name: "zones");
        }
    }
}
