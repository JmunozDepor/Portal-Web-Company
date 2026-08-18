using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.Postgres.Migrations
{
    /// <summary>
    /// Migración de DATOS, sin cambio de esquema -- backfill de rol "Rendidor" para
    /// todo (company_id, user_id) que ya aparezca en expense_report_lines/
    /// expense_reports/expense_funds, es decir, cualquiera que ya venía usando el
    /// módulo antes de que "Rendidor" dejara de ser implícito (ver
    /// Models.RendicionesRoles y RendicionesRendidorPageModelBase). Sin esto, activar
    /// el gate habría bloqueado de golpe a todos los usuarios reales existentes.
    /// </summary>
    public partial class AddRendicionesUserRolesRendidorBackfill : Migration
    {
        private const string BackfillSql = @"
INSERT INTO rendiciones_user_roles (company_id, user_id, role)
SELECT DISTINCT src.company_id, src.user_id, 'Rendidor'
FROM (
    SELECT company_id, user_id FROM expense_report_lines
    UNION
    SELECT company_id, user_id FROM expense_reports
    UNION
    SELECT company_id, user_id FROM expense_funds
) AS src
WHERE NOT EXISTS (
    SELECT 1 FROM rendiciones_user_roles r
    WHERE r.company_id = src.company_id AND r.user_id = src.user_id AND r.role = 'Rendidor'
);";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort: borra toda fila "Rendidor" -- no distingue el backfill de una
            // asignación manual hecha después, mismo trade-off que cualquier Down() de una
            // migración de datos (documentado, no un descuido).
            migrationBuilder.Sql("DELETE FROM rendiciones_user_roles WHERE role = 'Rendidor';");
        }
    }
}
