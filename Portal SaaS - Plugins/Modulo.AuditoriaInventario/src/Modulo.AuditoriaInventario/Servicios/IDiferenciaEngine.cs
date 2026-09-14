using Modulo.AuditoriaInventario.Data;

namespace Modulo.AuditoriaInventario.Servicios;

/// <summary>
/// Motor de diferencias: cruza InventoryCapture (agregado por sesión+sector+barcode)
/// contra FrozenInventoryLine del snapshot congelado que corresponde a la misma
/// sucursal + Nro. de Inventario de la sesión, y materializa el resultado en
/// InventoryDifference. Se dispara al cerrar una sesión (ver
/// AuditoriaInventarioApiService.UpsertSesionAsync). Recibe el DbContext ya abierto
/// en vez de crear el suyo -- funciona igual desde la API (sin sesión de portal) que
/// desde una futura pantalla del portal.
/// </summary>
public interface IDiferenciaEngine
{
    Task<int> CalcularDiferenciasAsync(AuditoriaInventarioDbContext db, Guid sessionId, CancellationToken ct = default);
}
