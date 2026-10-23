using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class EquivalenciasComuna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commune_aliases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    text = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    normalized_text = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    commune_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commune_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_commune_aliases_communes",
                        column: x => x.commune_id,
                        principalTable: "communes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commune_aliases_commune_id",
                table: "commune_aliases",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_commune_aliases_company_id",
                table: "commune_aliases",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_commune_aliases_company_id_normalized_text",
                table: "commune_aliases",
                columns: new[] { "company_id", "normalized_text" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commune_aliases");
        }
    }
}
