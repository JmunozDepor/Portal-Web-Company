namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Valores por defecto que SAP ya tiene configurados nativamente para un socio de
/// negocio (OCRD.SlpCode/ListNum) -- portado de SocioNegocioDefaultsDto en
/// referencia-original/PortalSAP_v2. Sirve tanto para Cliente como para Proveedor
/// (OCRD es la misma tabla, distinguidos por CardType). Los formularios genéricos de
/// Venta usan PriceListCode como lista por defecto (modificable) para resolver el precio
/// de cada línea (precio especial del socio primero, ver IPriceListService); fuera de
/// ahí, un documento sin UnitPrice explícito deja que Service Layer asigne el precio de
/// la lista del socio al postear.
///
/// Non-positional a propósito -- HanaService lo instancia vía reflection (ver
/// GeneralLedgerAccountDto/CostCenterDto, mismo criterio).
/// </summary>
public sealed record BusinessPartnerDefaultsDto
{
    public string CardCode { get; init; } = string.Empty;
    public int? SalesEmployeeCode { get; init; }
    public string? SalesEmployeeName { get; init; }
    public int? PriceListCode { get; init; }

    /// <summary>OCRD.ShipToDef -- código (CRD1.Address) de la dirección de despacho por defecto del socio.</summary>
    public string? ShipToDefault { get; init; }

    /// <summary>OCRD.Currency -- moneda del socio; "##" = acepta todas las monedas (sin moneda fija).</summary>
    public string? Currency { get; init; }

    /// <summary>OCRD.LicTradNum -- RUT del socio (documento tributario de las facturas de proveedor).</summary>
    public string? FederalTaxId { get; init; }
}
