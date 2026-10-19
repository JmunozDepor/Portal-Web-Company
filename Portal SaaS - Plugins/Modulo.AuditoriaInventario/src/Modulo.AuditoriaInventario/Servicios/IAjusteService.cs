using Modulo.AuditoriaInventario.Data;

namespace Modulo.AuditoriaInventario.Servicios;

/// <summary>
/// Aprobación manual de un InventoryAdjustment: resuelve el mapeo SAP
/// (SapCompanyCode/SapWarehouseCode desde Branch, SapMaterialCode desde Product) y
/// genera la fila en SapAdjustmentQueueItem -- el único punto donde algo entra a esa
/// cola, decisión explícita del dueño del proyecto (nunca envío automático).
/// </summary>
public interface IAjusteService
{
    Task<AprobarAjusteResultado> AprobarAsync(AuditoriaInventarioDbContext db, long adjustmentId, Guid companyId, Guid approvedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Crea un InventoryAdjustment PROPOSED por cada InventoryDifference de la
    /// sesión con diferencia real (QuantityDiff != 0) que todavía no tenga uno --
    /// idempotente, reintentarlo no duplica. Es el paso que faltaba entre
    /// IDiferenciaEngine (calcula InventoryDifference al cerrar) y la aprobación
    /// manual de AprobarAsync: sin esto, una diferencia calculada nunca se vuelve
    /// candidata a ajuste (encontrado 2026-10-15: Pages/Ajustes nunca mostraba nada
    /// porque nada en el código de producción creaba un InventoryAdjustment).
    /// Devuelve cuántos se crearon.
    /// </summary>
    Task<int> ProponerTodosAsync(AuditoriaInventarioDbContext db, Guid sessionId, Guid companyId, Guid proposedByUserId, CancellationToken ct = default);
}

public sealed record AprobarAjusteResultado(bool Exitoso, string Mensaje);
