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

    public bool AppliesTax { get; set; } = true;

    public decimal TaxPercentage { get; set; } = 19.00m;

    public bool IsActive { get; set; } = true;
}
