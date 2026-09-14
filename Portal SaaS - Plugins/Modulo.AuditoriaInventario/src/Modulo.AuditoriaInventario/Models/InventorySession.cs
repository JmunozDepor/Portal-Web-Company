namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Sesión de conteo físico. Id es GUID GENERADO EN EL CLIENTE (PWA) -- se crea sin
/// conexión en bodega/sala de venta, antes de sincronizar con este backend. Usar
/// bigint identity acá rompería apenas dos capturadores abrieran sesión offline al
/// mismo tiempo (colisión de Id al sincronizar). Mismo criterio en InventoryCapture.Id.
/// </summary>
public class InventorySession
{
    public Guid Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required long BranchId { get; set; }

    public required string InventoryNumber { get; set; }

    public required long ResponsibleUserId { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>ACTIVE | CLOSED | SYNCED.</summary>
    public string Status { get; set; } = "ACTIVE";

    /// <summary>
    /// El toggle pedido explícitamente: si es false, la captura no valida contra
    /// Product y InventoryCapture.InMaster queda null (no evaluado) en vez de false
    /// (no encontrado) -- distinción importante para no reportar "faltantes en
    /// maestro" falsos cuando la validación estuvo apagada a propósito.
    /// </summary>
    public bool ValidateAgainstMaster { get; set; } = true;

    /// <summary>Snapshot de con qué versión del maestro se auditó -- trazabilidad si Product cambia entre sesiones.</summary>
    public DateTimeOffset? MasterSnapshotAt { get; set; }
}
