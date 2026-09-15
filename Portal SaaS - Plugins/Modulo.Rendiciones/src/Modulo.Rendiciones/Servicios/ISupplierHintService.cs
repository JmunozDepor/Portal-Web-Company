using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Memoria aprendida por proveedor (RUT) por compañía -- ver <see cref="SupplierHint"/>.
/// Se lee al capturar un comprobante (para proponer campos) y se escribe SOLO cuando el
/// usuario confirma un gasto (guardar en Detalle / Importar).
/// </summary>
public interface ISupplierHintService
{
    /// <summary>Hint para un RUT ya normalizado ("12345678-9"). Null si no hay o el RUT es inválido.</summary>
    Task<SupplierHint?> GetAsync(Guid companyId, string normalizedRut, CancellationToken ct = default);

    /// <summary>
    /// Aprende de un guardado confirmado: sube el nombre, cuenta la categoría (recalcula
    /// la moda) y fija el tipo de documento habitual. No-op silencioso si el RUT no
    /// valida (dígito verificador). <paramref name="rawRut"/> puede venir con puntos/formato.
    /// </summary>
    Task RegisterAsync(Guid companyId, string? rawRut, string? supplierName,
        long? expenseTypeId, long? documentTypeId, CancellationToken ct = default);
}
