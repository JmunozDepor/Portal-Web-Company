using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modulo.Wms.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsExportValidations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wms_oracle_export_validations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_doc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    clave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    wms_status_id = table.Column<int>(type: "integer", nullable: true),
                    wms_status_desc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    wms_error_msg = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    validado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    intentos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_oracle_export_validations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_export_validations_company_tipodoc_clave",
                table: "wms_oracle_export_validations",
                columns: new[] { "company_id", "tipo_doc", "clave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_oracle_export_validations");
        }
    }
}
