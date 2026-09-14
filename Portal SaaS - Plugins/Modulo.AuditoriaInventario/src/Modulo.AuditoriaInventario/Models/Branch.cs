namespace Modulo.AuditoriaInventario.Models;

/// <summary>Sucursal propia del módulo (catálogo simple: código + nombre).</summary>
public class Branch
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required string BranchCode { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    public string? SapCompanyCode { get; set; }

    public string? SapWarehouseCode { get; set; }
}
