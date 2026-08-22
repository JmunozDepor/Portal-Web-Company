using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modulo.Wms.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsValidationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wms_validation_fields",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_entidad = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_validation_fields", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uk_wms_validation_fields_key",
                table: "wms_validation_fields",
                columns: new[] { "company_id", "tipo_entidad", "field_name" },
                unique: true);

            migrationBuilder.InsertData(
                table: "wms_validation_fields",
                columns: new[] { "company_id", "tipo_entidad", "field_name", "is_active" },
                values: new object[,]
                {
                    { new Guid("0f5f72d4-abe6-4a75-bd26-c663848abbb4"), "Item", "description", true },
                    { new Guid("0f5f72d4-abe6-4a75-bd26-c663848abbb4"), "Item", "barcode", true },
                    { new Guid("0f5f72d4-abe6-4a75-bd26-c663848abbb4"), "Item", "brand_code", true },
                    { new Guid("0f5f72d4-abe6-4a75-bd26-c663848abbb4"), "Item", "putaway_type", true },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_validation_fields");
        }
    }
}
