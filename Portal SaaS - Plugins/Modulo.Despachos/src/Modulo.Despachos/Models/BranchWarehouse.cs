namespace Modulo.Despachos.Models;

/// <summary>
/// Bodega SAP que es sucursal: solo las transferencias a estas bodegas llevan guía de traslado (el resto son
/// almacenes virtuales o de uso interno). Dirección y comuna se precargan de OWHS y se pueden corregir.
/// </summary>
public class BranchWarehouse : AuditedEntity
{
    public string WarehouseCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? CommuneName { get; set; }
    public bool IsActive { get; set; } = true;
}
