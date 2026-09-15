namespace Modulo.Rendiciones.Models;

/// <summary>
/// Tipo de documento tributario del país (Boleta, Factura, etc.), con su IVA. Tabla de
/// mantención local por compañía, sin relación con ninguna dimensión SAP -- mismo
/// criterio que ExpenseType. Portado de TipoDocumento (PortalSAP_v2).
/// </summary>
public class DocumentType
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Código SII del tipo de DTE (33 factura afecta, 34 factura exenta, 39 boleta
    /// afecta, 41 boleta exenta, 61 nota de crédito, 52 guía de despacho...). Nullable:
    /// una fila de mantención local puede no corresponder a un DTE. Lo usa el OCR para
    /// mapear el tipo de documento leído a esta fila (ver ChileanDocumentKind).
    /// </summary>
    public int? SiiCode { get; set; }

    public bool AppliesTax { get; set; } = true;

    public decimal TaxPercentage { get; set; } = 19.00m;

    public bool IsActive { get; set; } = true;
}
