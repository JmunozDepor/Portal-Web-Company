using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class SeedFixedActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "actions",
                columns: new[] { "id", "code", "name" },
                values: new object[,]
                {
                    { 1L, "VIEW", "Ver" },
                    { 2L, "CREATE", "Crear" },
                    { 3L, "EDIT", "Editar" },
                    { 4L, "DELETE", "Eliminar" },
                    { 5L, "APPROVE", "Aprobar" },
                    { 6L, "EXPORT", "Exportar" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "actions",
                keyColumn: "id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "actions",
                keyColumn: "id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "actions",
                keyColumn: "id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "actions",
                keyColumn: "id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                table: "actions",
                keyColumn: "id",
                keyValue: 5L);

            migrationBuilder.DeleteData(
                table: "actions",
                keyColumn: "id",
                keyValue: 6L);
        }
    }
}
