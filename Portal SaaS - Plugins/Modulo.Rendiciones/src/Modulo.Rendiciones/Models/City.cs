namespace Modulo.Rendiciones.Models;

/// <summary>
/// Ciudad del catálogo local de la compañía, usada para acotar las políticas de gasto
/// por ciudad (tope por tipo de gasto x ciudad). Tabla de mantención simple, sin
/// relación con ninguna dimensión SAP -- mismo criterio que ExpenseType/DocumentType.
/// </summary>
public class City
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;
}
