using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class EntregasGuiaPrevia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "warehouse_code",
                table: "despachos_dispatch_groups",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<long>(
                name: "customer_po",
                table: "despachos_dispatch_groups",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "customer_po_date",
                table: "despachos_dispatch_groups",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "customer_tax_id",
                table: "despachos_dispatch_groups",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dispatch_type",
                table: "despachos_dispatch_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "despachos_dispatch_groups",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "transfer");

            migrationBuilder.AddColumn<int>(
                name: "transfer_indicator",
                table: "despachos_dispatch_groups",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "customer_po",
                table: "despachos_dispatch_groups");

            migrationBuilder.DropColumn(
                name: "customer_po_date",
                table: "despachos_dispatch_groups");

            migrationBuilder.DropColumn(
                name: "customer_tax_id",
                table: "despachos_dispatch_groups");

            migrationBuilder.DropColumn(
                name: "dispatch_type",
                table: "despachos_dispatch_groups");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "despachos_dispatch_groups");

            migrationBuilder.DropColumn(
                name: "transfer_indicator",
                table: "despachos_dispatch_groups");

            migrationBuilder.AlterColumn<string>(
                name: "warehouse_code",
                table: "despachos_dispatch_groups",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);
        }
    }
}
