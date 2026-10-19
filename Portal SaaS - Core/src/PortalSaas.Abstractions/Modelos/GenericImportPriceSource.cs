namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// De dónde sale el precio de una línea de Artículo cuando el archivo de importación no
/// trae UnitPrice -- ver GenericImportConfigDto.PriceSource. Portado de
/// OrigenPrecioImportacionGenerica.
/// </summary>
public enum GenericImportPriceSource
{
    /// <summary>
    /// Default -- se deja UnitPrice ausente en el POST a Service Layer y es SAP quien
    /// asigna el precio de la lista propia del socio de negocio (OCRD.ListNum), igual que
    /// la digitación manual.
    /// </summary>
    BusinessPartner,

    /// <summary>
    /// Fuerza el precio de una lista de precio fija (SystemPriceListCode), igual para
    /// cualquier socio de negocio, resuelto contra ITM1 antes de crear el documento (ver
    /// IPriceListService).
    /// </summary>
    System,

    /// <summary>
    /// Precio especial del socio (OSPP / descuentos por período SPP1, vigentes a la fecha)
    /// y, si no tiene, el de su lista (OCRD.ListNum) -- lo mismo que aplicaría SAP al crear
    /// el documento sin UnitPrice. Default de los formatos nuevos (2026-10-15).
    /// </summary>
    SpecialPrice,
}
