using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericImportModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "generic_import_configs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    line_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    business_partner_card_code = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    grouping_column = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    sku_is_customer_own = table.Column<bool>(type: "boolean", nullable: false),
                    alias = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    price_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    system_price_list_code = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generic_import_configs", x => x.id);
                    table.ForeignKey(
                        name: "fk_generic_import_configs_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generic_import_user_fields",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sap_field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    data_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generic_import_user_fields", x => x.id);
                    table.ForeignKey(
                        name: "fk_generic_import_user_fields_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generic_import_config_fields",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    config_id = table.Column<int>(type: "integer", nullable: false),
                    logical_field = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    excel_column = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    fixed_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    user_field_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generic_import_config_fields", x => x.id);
                    table.ForeignKey(
                        name: "fk_generic_import_config_fields_generic_import_configs_config_",
                        column: x => x.config_id,
                        principalTable: "generic_import_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_generic_import_config_fields_generic_import_user_fields_use",
                        column: x => x.user_field_id,
                        principalTable: "generic_import_user_fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_config_fields_config_id",
                table: "generic_import_config_fields",
                column: "config_id");

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_config_fields_user_field_id",
                table: "generic_import_config_fields",
                column: "user_field_id");

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_configs_organization_id_module_document_type",
                table: "generic_import_configs",
                columns: new[] { "organization_id", "module", "document_type", "line_type", "business_partner_card_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_user_fields_organization_id",
                table: "generic_import_user_fields",
                column: "organization_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generic_import_config_fields");

            migrationBuilder.DropTable(
                name: "generic_import_configs");

            migrationBuilder.DropTable(
                name: "generic_import_user_fields");
        }
    }
}
