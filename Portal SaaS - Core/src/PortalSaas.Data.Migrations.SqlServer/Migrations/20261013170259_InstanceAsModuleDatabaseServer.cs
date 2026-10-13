using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InstanceAsModuleDatabaseServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "ck_instances_engine_type", table: "instances");
            migrationBuilder.AddCheckConstraint(
                name: "ck_instances_engine_type",
                table: "instances",
                sql: "engine_type in ('hana', 'sqlserver', 'postgres')");

            migrationBuilder.AddColumn<long>(name: "instance_id", table: "company_module_connections", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<string>(name: "database_name", table: "company_module_connections", type: "nvarchar(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "technical_username", table: "company_module_connections", type: "nvarchar(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "technical_secret_key", table: "company_module_connections", type: "nvarchar(500)", maxLength: 500, nullable: true);

            migrationBuilder.Sql(@"
INSERT INTO instances (organization_id, name, host, port, engine_type, technical_username, technical_secret_key, is_active)
SELECT g.organization_id,
       CONCAT(LEFT(CONCAT(g.engine_type, ' ', g.host, ':', g.port, ' ', g.technical_username), 42), ' #', g.rn),
       g.host, g.port, g.engine_type, g.technical_username, g.technical_secret_key, 1
FROM (
    SELECT s.organization_id, s.engine_type, s.host, s.port, s.technical_username,
           COALESCE(MIN(NULLIF(s.technical_secret_key, '')), '') AS technical_secret_key,
           ROW_NUMBER() OVER (PARTITION BY s.organization_id ORDER BY s.engine_type, s.host, s.port, s.technical_username) AS rn
    FROM (
        SELECT c.organization_id,
               CASE x.tipo WHEN 'db_postgres' THEN 'postgres' WHEN 'db_sqlserver' THEN 'sqlserver' ELSE 'hana' END AS engine_type,
               COALESCE(x.host, '') AS host,
               COALESCE(x.port, CASE x.tipo WHEN 'db_postgres' THEN 5432 WHEN 'db_sqlserver' THEN 1433 ELSE 30015 END) AS port,
               COALESCE(x.technical_username, '') AS technical_username,
               x.technical_secret_key AS technical_secret_key
        FROM company_external_connections x
        JOIN companies c ON c.id = x.company_id
        WHERE x.tipo IN ('db_postgres', 'db_sqlserver', 'db_hana') AND x.is_active = 1
    ) s
    GROUP BY s.organization_id, s.engine_type, s.host, s.port, s.technical_username
) g
WHERE NOT EXISTS (
    SELECT 1 FROM instances i
    WHERE i.organization_id = g.organization_id AND i.engine_type = g.engine_type
      AND i.host = g.host AND i.port = g.port AND i.technical_username = g.technical_username);
");

            migrationBuilder.Sql(@"
UPDATE b
SET b.instance_id = i.id,
    b.database_name = COALESCE(x.database_name, ''),
    b.technical_username = CASE WHEN NULLIF(x.technical_secret_key, '') IS NOT NULL AND x.technical_secret_key <> i.technical_secret_key THEN COALESCE(x.technical_username, '') ELSE NULL END,
    b.technical_secret_key = CASE WHEN NULLIF(x.technical_secret_key, '') IS NOT NULL AND x.technical_secret_key <> i.technical_secret_key THEN x.technical_secret_key ELSE NULL END
FROM company_module_connections b
JOIN company_external_connections x ON x.id = b.connection_id
JOIN companies c ON c.id = x.company_id
JOIN instances i
  ON i.organization_id = c.organization_id
 AND i.engine_type = CASE x.tipo WHEN 'db_postgres' THEN 'postgres' WHEN 'db_sqlserver' THEN 'sqlserver' ELSE 'hana' END
 AND i.host = COALESCE(x.host, '')
 AND i.port = COALESCE(x.port, CASE x.tipo WHEN 'db_postgres' THEN 5432 WHEN 'db_sqlserver' THEN 1433 ELSE 30015 END)
 AND i.technical_username = COALESCE(x.technical_username, '')
WHERE x.tipo IN ('db_postgres', 'db_sqlserver', 'db_hana')
  AND x.is_active = 1;
");

            migrationBuilder.Sql("DELETE FROM company_module_connections WHERE instance_id IS NULL OR purpose <> 'Default';");

            migrationBuilder.DropForeignKey(
                name: "fk_company_module_connections_company_external_connections_connection_id",
                table: "company_module_connections");
            migrationBuilder.DropIndex(name: "ix_company_module_connections_connection_id", table: "company_module_connections");
            migrationBuilder.DropIndex(name: "uq_company_module_connections_company_module_purpose", table: "company_module_connections");
            migrationBuilder.DropColumn(name: "connection_id", table: "company_module_connections");
            migrationBuilder.DropColumn(name: "purpose", table: "company_module_connections");

            migrationBuilder.AlterColumn<long>(
                name: "instance_id", table: "company_module_connections", type: "bigint", nullable: false,
                oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "database_name", table: "company_module_connections", type: "nvarchar(100)", maxLength: 100, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100, oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_company_module_connections_company_module",
                table: "company_module_connections",
                columns: new[] { "company_id", "module_code" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "ix_company_module_connections_instance_id",
                table: "company_module_connections",
                column: "instance_id");
            migrationBuilder.AddForeignKey(
                name: "fk_company_module_connections_instances_instance_id",
                table: "company_module_connections",
                column: "instance_id",
                principalTable: "instances",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropTable(name: "company_external_connections");
            migrationBuilder.DropTable(name: "module_external_connections");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "InstanceAsModuleDatabaseServer no es reversible: restaurar el backup previo a la migración.");
        }
    }
}
