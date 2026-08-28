using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.SqlServer.Migrations
{
    /// <summary>
    /// Migración de DATOS, sin cambio de esquema -- ver el comentario largo en la
    /// migración homónima de Modulo.Rendiciones.Migrations.Postgres, mismo SQL (ANSI
    /// estándar, sin nada específico de un motor).
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
            migrationBuilder.Sql("DELETE FROM rendiciones_user_roles WHERE role = 'Rendidor';");
        }
    }
}
