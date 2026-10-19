using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFormFieldLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_field_layouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    form_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    hidden_fields = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_field_layouts", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_field_layouts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_form_field_layouts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_field_layouts_company_id_form_key_user_id",
                table: "form_field_layouts",
                columns: new[] { "company_id", "form_key", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_form_field_layouts_user_id",
                table: "form_field_layouts",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_field_layouts");
        }
    }
}
