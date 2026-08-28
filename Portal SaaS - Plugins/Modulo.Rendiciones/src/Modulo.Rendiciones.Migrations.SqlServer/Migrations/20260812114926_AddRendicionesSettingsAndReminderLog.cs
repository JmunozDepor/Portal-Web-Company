using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRendicionesSettingsAndReminderLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rendiciones_reminder_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sent_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rendiciones_reminder_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rendiciones_settings",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reminder_hour = table.Column<TimeOnly>(type: "time", nullable: false),
                    reminder_enabled = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rendiciones_settings", x => x.company_id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_rendiciones_reminder_log_company_id_sent_date",
                table: "rendiciones_reminder_log",
                columns: new[] { "company_id", "sent_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rendiciones_reminder_log");

            migrationBuilder.DropTable(
                name: "rendiciones_settings");
        }
    }
}
