namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Qué trae la columna de artículo (GenericImportLogicalField.ItemCode) del Excel y cómo
/// se traduce al ItemCode de SAP -- reemplaza al antiguo bool SkuIsCustomerOwn
/// (2026-10-14), que solo distinguía ItemCode vs. SKU del cliente.
/// </summary>
public enum GenericImportItemIdentifier
{
    /// <summary>El archivo trae el ItemCode de SAP tal cual (OITM.ItemCode).</summary>
    ItemCode,

    /// <summary>El archivo trae un código de barras: OITM.CodeBars (barra principal) u OBCD (barras adicionales por unidad de medida).</summary>
    Barcode,

    /// <summary>El archivo trae el SKU propio del cliente/proveedor: paridad AlternateCatNum (OSCN) del socio.</summary>
    CustomerSku,
}
