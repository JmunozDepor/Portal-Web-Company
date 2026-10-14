using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ImportItemIdentifierAndMultiGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // sku_is_customer_own (bool) -> item_identifier ("ItemCode" | "Barcode" |
            // "CustomerSku"). Crear la columna nueva y traspasar los formatos que usaban
            // SKU del cliente. La columna vieja NO se borra acá a propósito: un Host con el
            // código anterior (ej. producción todavía sin desplegar) la sigue leyendo --
            // borrarla lo rompería. Ya no está mapeada en el modelo (por eso el snapshot no
            // la tiene); se le pone DEFAULT para que los INSERT del código nuevo no fallen
            // por NOT NULL. Se elimina en una migración de limpieza posterior al despliegue.
            migrationBuilder.AddColumn<string>(
                name: "item_identifier",
                table: "generic_import_configs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ItemCode");

            migrationBuilder.Sql("UPDATE generic_import_configs SET item_identifier = 'CustomerSku' WHERE sku_is_customer_own;");

            migrationBuilder.Sql("ALTER TABLE generic_import_configs ALTER COLUMN sku_is_customer_own SET DEFAULT false;");

            // Varias columnas de agrupación separadas por coma (ej. "A,B").
            migrationBuilder.AlterColumn<string>(
                name: "grouping_column",
                table: "generic_import_configs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // sku_is_customer_own nunca se borró en Up (ver comentario ahí) -- solo se
            // resincroniza desde item_identifier antes de quitar la columna nueva.
            migrationBuilder.Sql("UPDATE generic_import_configs SET sku_is_customer_own = true WHERE item_identifier = 'CustomerSku';");

            migrationBuilder.DropColumn(
                name: "item_identifier",
                table: "generic_import_configs");

            // Ojo: un grouping_column de más de 10 caracteres (varias columnas) no entra
            // en el largo anterior -- revertir exige acortarlos antes.
            migrationBuilder.AlterColumn<string>(
                name: "grouping_column",
                table: "generic_import_configs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }
    }
}
