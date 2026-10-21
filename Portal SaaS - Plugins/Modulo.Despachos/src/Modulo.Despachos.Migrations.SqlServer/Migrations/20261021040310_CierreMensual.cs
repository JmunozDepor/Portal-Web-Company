using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class CierreMensual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monthly_closures",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    period = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    closed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    closed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reopened_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reopened_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reopen_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    manifest_count = table.Column<int>(type: "int", nullable: false),
                    total_cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_closures", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monthly_closures_company_id",
                table: "monthly_closures",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_monthly_closures_company_id_period",
                table: "monthly_closures",
                columns: new[] { "company_id", "period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monthly_closures");
        }
    }
}
