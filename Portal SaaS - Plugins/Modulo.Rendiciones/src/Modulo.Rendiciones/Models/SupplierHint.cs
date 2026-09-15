namespace Modulo.Rendiciones.Models;

/// <summary>
/// Memoria por compañía de lo que se aprendió de un proveedor (identificado por su
/// RUT normalizado): su nombre, su categoría de gasto habitual y su tipo de documento
/// habitual. Se actualiza SOLO desde guardados confirmados por el usuario (ver
/// ISupplierHintService.RegisterAsync), no del OCR crudo. Sirve para proponer/precargar
/// campos en la próxima captura del mismo proveedor.
/// </summary>
public class SupplierHint
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    /// <summary>RUT del proveedor normalizado a "12345678-9" (sin puntos, DV en mayúscula). Clave junto con CompanyId.</summary>
    public required string SupplierTaxId { get; set; }

    /// <summary>Último nombre confirmado para este RUT.</summary>
    public string? SupplierName { get; set; }

    /// <summary>Categoría propuesta = la más usada (moda) para este proveedor. Se recalcula en cada RegisterAsync.</summary>
    public long? DefaultExpenseTypeId { get; set; }

    /// <summary>Veces que se confirmó <see cref="DefaultExpenseTypeId"/> -- confianza de la propuesta (se pre-selecciona con &gt;= 2).</summary>
    public int TimesCategoryConfirmed { get; set; }

    /// <summary>Tipo de documento habitual (el último confirmado -- es estable por proveedor).</summary>
    public long? DefaultDocumentTypeId { get; set; }

    /// <summary>
    /// Conteo por categoría {expenseTypeId: veces} serializado a JSON -- historial mínimo
    /// para calcular la moda sin una tabla hija. Lo mapea un value converter en el
    /// DbContext.
    /// </summary>
    public Dictionary<long, int> CategoryCounts { get; set; } = new();

    public int TimesSeen { get; set; }

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
