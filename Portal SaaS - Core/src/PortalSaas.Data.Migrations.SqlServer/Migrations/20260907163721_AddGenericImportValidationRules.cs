using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericImportValidationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "generic_import_validation_rule_assignments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    config_id = table.Column<int>(type: "int", nullable: false),
                    rule_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    parameters_json = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generic_import_validation_rule_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_generic_import_validation_rule_assignments_generic_import_configs_config_id",
                        column: x => x.config_id,
                        principalTable: "generic_import_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_validation_rule_assignments_config_id_rule_type",
                table: "generic_import_validation_rule_assignments",
                columns: new[] { "config_id", "rule_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generic_import_validation_rule_assignments");
        }
    }
}
