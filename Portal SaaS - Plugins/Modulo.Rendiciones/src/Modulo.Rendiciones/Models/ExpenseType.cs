namespace Modulo.Rendiciones.Models;

/// <summary>
/// Catálogo de mantención local (Combustible, Peaje, Alojamiento, Almuerzo, etc.).
/// Sin relación con ninguna dimensión SAP. Portado de TipoGasto (PortalSAP_v2).
/// </summary>
public class ExpenseType
{
    public long Id { get; set; }

    /// <summary>Compañía SAP dueña de este catálogo -- resuelve a organization_id vía companies.organization_id (ver docs/01-CONVENCION-NOMBRES-BD.md, "organization_id directo o vía company_id").</summary>
    public required Guid CompanyId { get; set; }

    public required string Name { get; set; }

    /// <summary>Cuenta contable SAP para centralizar/contabilizar el gasto por categoría. Opcional.</summary>
    public string? SapGlAccount { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Si es true, un gasto de este tipo se captura con Origin/Destination en vez de Amount/comprobante -- el monto se calcula (DistanceKm * RatePerKm) al guardar.</summary>
    public bool IsMileage { get; set; }

    /// <summary>Requerido si IsMileage -- moneda local por km. Nullable porque un tipo normal nunca lo usa.</summary>
    public decimal? RatePerKm { get; set; }
}
