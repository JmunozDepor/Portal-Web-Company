using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDteReceivedPaymentInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                table: "dte_received",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_form",
                table: "dte_received",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "due_date",
                table: "dte_received");

            migrationBuilder.DropColumn(
                name: "payment_form",
                table: "dte_received");
        }
    }
}
