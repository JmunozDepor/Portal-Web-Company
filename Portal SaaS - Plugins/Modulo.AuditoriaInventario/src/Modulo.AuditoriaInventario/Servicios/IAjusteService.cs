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
    Task<AprobarAjusteResultado> AprobarAsync(AuditoriaInventarioDbContext db, long adjustmentId, Guid approvedByUserId, CancellationToken ct = default);
}

public sealed record AprobarAjusteResultado(bool Exitoso, string Mensaje);
