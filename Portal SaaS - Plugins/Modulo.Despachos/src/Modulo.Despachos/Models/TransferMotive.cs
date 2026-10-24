namespace Modulo.Despachos.Models;

/// <summary>
/// Motivo de traslado (SII IndTraslado) habilitado para guías manuales. <see cref="VatAffected"/> decide el IVA de la
/// guía: afecto = neto + IVA 19%; no afecto = líneas exentas (total = neto).
/// </summary>
public class TransferMotive : AuditedEntity
{
    public int Code { get; set; }
    public string Name { get; set; } = "";
    public bool VatAffected { get; set; }
    public bool IsActive { get; set; } = true;
}
