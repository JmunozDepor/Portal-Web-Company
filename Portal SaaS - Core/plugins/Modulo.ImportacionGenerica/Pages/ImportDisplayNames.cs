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
}
