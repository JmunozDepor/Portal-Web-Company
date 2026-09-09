using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSessionLastSeenAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_at",
                table: "user_sessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Backfill: las filas previas a esta migración representan sesiones cuya
            // última actividad conocida es su propio login -- sin esto quedarían en
            // 0001-01-01 y la purga las borraría de una (incluida la de un usuario
            // logueado justo al desplegar). Mismo criterio que AddPlanToOnPremiseLicense.
            migrationBuilder.Sql("UPDATE user_sessions SET last_seen_at = created_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_seen_at",
                table: "user_sessions");
        }
    }
}
