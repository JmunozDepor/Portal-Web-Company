namespace Modulo.Despachos.Models;

/// <summary>Ajustes del módulo por compañía (una fila por compañía).</summary>
public class DespachosSetting : AuditedEntity
{
    /// <summary>RUT de la compañía como emisor de los DTE (se compara con el RE del timbre).</summary>
    public string? IssuerTaxId { get; set; }

    /// <summary>
    /// Emitir las guías de despacho electrónicas (Res. 154) al despachar. Apagado = comportamiento de la
    /// Etapa 1 (ej. una compañía cuyo proveedor DTE aún no está integrado).
    /// </summary>
    public bool GuidesEnabled { get; set; }

    /// <summary>IndTraslado de la guía de mercadería ya facturada (1 = operación constituye venta) [validar con el asesor].</summary>
    public int TransferIndicator { get; set; } = 1;

    /// <summary>TipoDespacho (1 por cuenta del receptor, 2 emisor a cliente, 3 emisor a otras instalaciones); null = no se informa.</summary>
    public int? DispatchType { get; set; } = 2;

    /// <summary>Resumen de las líneas de la guía: GuideLineGroupings.Sku (default) o .Model.</summary>
    public string GuideLineGrouping { get; set; } = GuideLineGroupings.Sku;

    /// <summary>Código del artículo en la guía: GuideItemCodePlacements.Column (default) o .Name.</summary>
    public string GuideItemCodePlacement { get; set; } = GuideItemCodePlacements.Column;

    /// <summary>
    /// Máximo de líneas por guía (1..60). 60 es el máximo del formato SII; menos cuando la plantilla PDF del
    /// proveedor no alcanza a imprimirlas sin montarlas sobre el pie (FACELE: 27).
    /// </summary>
    public int GuideMaxLines { get; set; } = 60;

    /// <summary>
    /// Valorizar la guía con los montos netos de las facturas (columna "line_amount" de la consulta de líneas).
    /// Apagado = precios vacíos y montos 0.
    /// </summary>
    public bool GuideValued { get; set; }

    // ---------------------------------------------------------------- Traslados a sucursales (Fase B)

    /// <summary>Expresión regular que reconoce un LPN en el escáner (ej. ^(CTBO|ICBO)\d+$); vacío = todo texto no-timbre.</summary>
    public string? LpnPattern { get; set; }

    /// <summary>FolioPref que se graba en la transferencia SAP junto al folio de la guía; vacío = no se graba el folio.</summary>
    public string? TransferFolioPrefix { get; set; } = "52";

    /// <summary>Campos de usuario de OWTR opcionales para el N° de manifiesto y la fecha de despacho (ej. U_NX_Manifiesto).</summary>
    public string? TransferManifestField { get; set; }
    public string? TransferDispatchDateField { get; set; }

    /// <summary>IndTraslado y TipoDespacho de la guía de traslado interno (5 y 3 por defecto).</summary>
    public int InternalTransferIndicator { get; set; } = 5;
    public int? InternalDispatchType { get; set; } = 3;
}
