namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Valores por defecto que SAP ya tiene configurados nativamente para un socio de
/// negocio (OCRD.SlpCode/ListNum) -- portado de SocioNegocioDefaultsDto en
/// referencia-original/PortalSAP_v2. Sirve tanto para Cliente como para Proveedor
/// (OCRD es la misma tabla, distinguidos por CardType). El precio NUNCA se calcula a
/// mano con PriceListCode -- es solo un valor de referencia informativo; el documento
/// se crea con UnitPrice ausente cuando no hay precio explícito y es Service Layer
/// quien asigna el precio de la lista del socio al postear (ver
/// SalesDocumentService/PurchaseDocumentService).
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
}
