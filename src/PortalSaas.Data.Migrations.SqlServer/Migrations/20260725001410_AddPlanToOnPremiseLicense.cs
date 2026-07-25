using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanToOnPremiseLicense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "plan_id",
                table: "on_premise_licenses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Backfill de licencias emitidas antes de que plan_id existiera -- se les
            // asigna el plan más antiguo (el único que existía hasta ahora en cada
            // instalación real). Sin esto, el FK de abajo falla contra cualquier fila
            // existente (0 no es un id de plan válido).
            migrationBuilder.Sql(
                "UPDATE on_premise_licenses SET plan_id = (SELECT TOP 1 id FROM plans ORDER BY id) " +
                "WHERE plan_id = 0 AND EXISTS (SELECT 1 FROM plans);");

            migrationBuilder.CreateIndex(
                name: "ix_on_premise_licenses_plan_id",
                table: "on_premise_licenses",
                column: "plan_id");

            migrationBuilder.AddForeignKey(
                name: "fk_on_premise_licenses_plans_plan_id",
                table: "on_premise_licenses",
                column: "plan_id",
                principalTable: "plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_on_premise_licenses_plans_plan_id",
                table: "on_premise_licenses");

            migrationBuilder.DropIndex(
                name: "ix_on_premise_licenses_plan_id",
                table: "on_premise_licenses");

            migrationBuilder.DropColumn(
                name: "plan_id",
                table: "on_premise_licenses");
        }
    }
}
