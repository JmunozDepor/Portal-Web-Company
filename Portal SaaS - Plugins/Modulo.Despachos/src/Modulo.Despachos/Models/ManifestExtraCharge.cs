namespace Modulo.Despachos.Models;

/// <summary>
/// Cantidad que logística ingresa en el manifiesto para un recargo "manual_quantity" del tarifario
/// (p. ej. 2 × Ayudante adicional, 1 × Punto adicional). Una fila por manifiesto y recargo; cantidad 0 no se guarda.
/// </summary>
public class ManifestExtraCharge
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ManifestId { get; set; }
    public long RateSurchargeId { get; set; }
    public int Quantity { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
