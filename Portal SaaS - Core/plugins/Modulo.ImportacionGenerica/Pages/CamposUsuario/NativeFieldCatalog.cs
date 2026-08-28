namespace Modulo.ImportacionGenerica.Pages.CamposUsuario;

/// <summary>
/// Lista ESTÁTICA y explícitamente NO exhaustiva de nombres de propiedad nativos
/// comunes de Service Layer, usada solo para precargar el &lt;datalist&gt; de "Nombre
/// de campo (Service Layer)" en Index.cshtml -- el campo sigue siendo texto libre, esto
/// es ayuda para no memorizar nombres, nunca un gate. Portado de
/// CampoNativoSapCatalogo (referencia-original/PortalSAP_v2).
///
/// Decisión ya discutida y descartada en el original: no se valida en duro contra el
/// esquema real de SAP (ni HANA SYS.TABLE_COLUMNS ni $metadata de Service Layer) --
/// el nombre de columna HANA casi nunca coincide con el nombre de propiedad de Service
/// Layer (ej. ToWhsCode en HANA vs. ToWarehouse en Service Layer), así que una
/// validación en duro arriesgaba bloquear campos nativos válidos solo por el desfase de
/// nombres.
/// </summary>
public static class NativeFieldCatalog
{
    /// <summary>Cabecera -- Venta/Compra.</summary>
    public static readonly IReadOnlyList<string> SalesPurchaseHeader =
    [
        "JournalMemo", "Series", "Confirmed", "DocRate", "ContactPersonCode", "Comments",
    ];

    /// <summary>Línea -- Venta/Compra.</summary>
    public static readonly IReadOnlyList<string> SalesPurchaseLine =
    [
        "FreeText", "Currency", "Rate", "ShipDate",
    ];

    /// <summary>Cabecera/línea -- Inventario, set más chico (StockTransfer no tiene la mayoría de los campos de arriba).</summary>
    public static readonly IReadOnlyList<string> Inventory =
    [
        "Series", "Comments", "FromWarehouse", "ToWarehouse",
    ];

    public static IReadOnlyList<string> All { get; } =
        SalesPurchaseHeader.Concat(SalesPurchaseLine).Concat(Inventory).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
}
