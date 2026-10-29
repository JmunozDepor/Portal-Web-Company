using System.Globalization;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica.Pages;

/// <summary>
/// Nombre visible de los valores de enum que este plugin muestra en pantalla (Módulo,
/// Tipo de documento, Tipo de línea) en el idioma configurado del usuario
/// (CultureInfo.CurrentUICulture, fijada por UserProfileCultureProvider del Host a
/// partir de la preferencia Locale). Antes los &lt;select&gt; y las tablas mostraban
/// enum.ToString() crudo ("Inventory", "StockTransfer") sin importar el idioma.
/// El VALUE de cada &lt;option&gt; sigue siendo el nombre del enum -- solo cambia el
/// texto. Sin .resx propio: el plugin vive en su propio AssemblyLoadContext y no puede
/// usar SharedResources del Host; con 2 idiomas soportados alcanza un diccionario.
/// Idioma no soportado o sin traducción -> español (default del portal), y si tampoco
/// hay, el nombre del enum tal cual.
/// </summary>
public static class ImportDisplayNames
{
    private static readonly Dictionary<string, (string Es, string En)> Names = new()
    {
        // Módulo
        [nameof(GenericImportModule.Sales)] = ("Ventas", "Sales"),
        [nameof(GenericImportModule.Purchase)] = ("Compras", "Purchasing"),
        [nameof(GenericImportModule.Inventory)] = ("Inventario", "Inventory"),

        // Tipo de línea
        [nameof(GenericImportLineType.Item)] = ("Artículo", "Item"),
        [nameof(GenericImportLineType.Service)] = ("Servicio", "Service"),

        // Tipos de documento de Venta
        [nameof(SalesDocumentType.SalesOrder)] = ("Orden de venta", "Sales order"),
        [nameof(SalesDocumentType.CreditNote)] = ("Nota de crédito", "Credit note"),
        [nameof(SalesDocumentType.CustomerInvoice)] = ("Factura de cliente", "Customer invoice"),
        [nameof(SalesDocumentType.ReserveInvoice)] = ("Factura de reserva", "Reserve invoice"),
        [nameof(SalesDocumentType.ReturnRequest)] = ("Solicitud de devolución", "Return request"),
        [nameof(SalesDocumentType.Return)] = ("Devolución", "Return"),
        [nameof(SalesDocumentType.Receipt)] = ("Boleta", "Receipt"),

        // Tipos de documento de Compra
        [nameof(PurchaseDocumentType.PurchaseQuotation)] = ("Oferta de compra", "Purchase quotation"),
        [nameof(PurchaseDocumentType.PurchaseOrder)] = ("Pedido de compra", "Purchase order"),
        [nameof(PurchaseDocumentType.PurchaseInvoice)] = ("Factura de proveedor", "Purchase invoice"),
        [nameof(PurchaseDocumentType.PurchaseReserveInvoice)] = ("Factura de reserva de proveedor", "Purchase reserve invoice"),

        // Tipos de documento de Inventario
        [nameof(InventoryDocumentType.InventoryTransferRequest)] = ("Solicitud de traslado", "Inventory transfer request"),
        [nameof(InventoryDocumentType.StockTransfer)] = ("Traslado de stock", "Stock transfer"),
    };

    private static bool IsEnglish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

    /// <summary>Nombre visible de un valor de enum (o su nombre como string, ej. GenericImportConfigDto.DocumentType).</summary>
    public static string Of(string? enumName)
    {
        if (string.IsNullOrEmpty(enumName))
        {
            return string.Empty;
        }

        return Names.TryGetValue(enumName, out var names) ? (IsEnglish ? names.En : names.Es) : enumName;
    }

    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum => Of(value.ToString());

    // Campos núcleo -- diccionario aparte porque algunos nombres chocan con otros enums
    // (ej. "ItemCode" es campo núcleo Y GenericImportItemIdentifier). Sap = nombre de la
    // propiedad de Service Layer donde termina el valor, para saber "a qué corresponde"
    // (pedido del dueño del proyecto, 2026-10-14).
    private static readonly Dictionary<GenericImportLogicalField, (string Es, string En, string Sap)> FieldNames = new()
    {
        [GenericImportLogicalField.CustomerReferenceNumber] = ("N° de orden / referencia del cliente", "Customer reference / order number", "NumAtCard"),
        [GenericImportLogicalField.Branch] = ("Sucursal (dirección de despacho del socio)", "Branch (partner ship-to address)", "ShipToCode"),
        [GenericImportLogicalField.BusinessPartnerCardCode] = ("Código de socio de negocio (por fila, carga multi-socio)", "Business partner code (per row, multi-partner)", "CardCode"),
        [GenericImportLogicalField.Quantity] = ("Cantidad", "Quantity", "Quantity"),
        [GenericImportLogicalField.UnitPrice] = ("Precio unitario (vacío = lista de precios del socio)", "Unit price (empty = partner price list)", "UnitPrice"),
        [GenericImportLogicalField.DiscountPercent] = ("% de descuento", "Discount %", "DiscountPercent"),
        [GenericImportLogicalField.ItemCode] = ("Artículo (código, barra o SKU según \"Identificación del artículo\")", "Item (code, barcode or SKU per \"Item identification\")", "ItemCode"),
        [GenericImportLogicalField.Warehouse] = ("Almacén (Venta/Compra)", "Warehouse (Sales/Purchasing)", "WarehouseCode"),
        [GenericImportLogicalField.Description] = ("Descripción (líneas de servicio)", "Description (service lines)", "ItemDescription"),
        [GenericImportLogicalField.Account] = ("Cuenta de mayor (líneas de servicio)", "G/L account (service lines)", "AccountCode"),
        [GenericImportLogicalField.CostCenter] = ("Centro de costos / dimensión 1", "Cost center / dimension 1", "CostingCode"),
        [GenericImportLogicalField.Dimension2] = ("Dimensión 2", "Dimension 2", "CostingCode2"),
        [GenericImportLogicalField.Dimension3] = ("Dimensión 3", "Dimension 3", "CostingCode3"),
        [GenericImportLogicalField.SourceWarehouse] = ("Almacén origen (Inventario)", "From warehouse (Inventory)", "FromWarehouseCode"),
        [GenericImportLogicalField.DestinationWarehouse] = ("Almacén destino (Inventario)", "To warehouse (Inventory)", "WarehouseCode"),
    };

    /// <summary>Descripción del campo núcleo en el idioma del usuario.</summary>
    public static string Field(GenericImportLogicalField field) =>
        FieldNames.TryGetValue(field, out var names) ? (IsEnglish ? names.En : names.Es) : field.ToString();

    /// <summary>Propiedad de Service Layer donde termina el campo (ej. Branch -> ShipToCode).</summary>
    public static string FieldSapName(GenericImportLogicalField field) =>
        FieldNames.TryGetValue(field, out var names) ? names.Sap : field.ToString();

    private static readonly Dictionary<GenericImportItemIdentifier, (string Es, string En)> ItemIdentifierNames = new()
    {
        [GenericImportItemIdentifier.ItemCode] = ("Código de artículo SAP (ItemCode)", "SAP item code (ItemCode)"),
        [GenericImportItemIdentifier.Barcode] = ("Código de barras (maestro de artículos: CodeBars / barras adicionales)", "Barcode (item master: CodeBars / additional barcodes)"),
        [GenericImportItemIdentifier.CustomerSku] = ("SKU del cliente/proveedor (paridad del socio)", "Customer/vendor SKU (partner cross-reference)"),
    };

    public static string Identifier(GenericImportItemIdentifier identifier) =>
        ItemIdentifierNames.TryGetValue(identifier, out var names) ? (IsEnglish ? names.En : names.Es) : identifier.ToString();
}
