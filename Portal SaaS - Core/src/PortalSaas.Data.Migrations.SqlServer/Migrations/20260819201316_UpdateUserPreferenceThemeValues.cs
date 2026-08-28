using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserPreferenceThemeValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_preferences_theme",
                table: "user_preferences");

            // Backfill de filas existentes con los 3 valores viejos (light/dark/system,
            // ninguno aplicado de verdad en la UI) antes de exigir el CHECK nuevo -- sin
            // esto, cualquier fila previa a esta migración violaría el constraint nuevo
            // al aplicarse (mismo criterio ya usado en AddPlanToOnPremiseLicense).
            migrationBuilder.Sql("UPDATE user_preferences SET theme = 'oscuro' WHERE theme = 'dark';");
            migrationBuilder.Sql("UPDATE user_preferences SET theme = 'claro' WHERE theme IN ('light', 'system');");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_preferences_theme",
                table: "user_preferences",
                sql: "theme in ('claro', 'oscuro', 'teal', 'violeta')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_preferences_theme",
                table: "user_preferences");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_preferences_theme",
                table: "user_preferences",
                sql: "theme in ('light', 'dark', 'system')");
        }
    }
}
