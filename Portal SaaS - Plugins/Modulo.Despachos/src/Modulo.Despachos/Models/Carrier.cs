namespace Modulo.Despachos.Models;

/// <summary>Transportista. RUT obligatorio salvo kind=pickup (Res.154 exige RUT transportista).</summary>
public class Carrier : AuditedEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? TaxId { get; set; }
    public string Kind { get; set; } = CarrierKinds.ThirdParty;
    public bool IsActive { get; set; } = true;
}
