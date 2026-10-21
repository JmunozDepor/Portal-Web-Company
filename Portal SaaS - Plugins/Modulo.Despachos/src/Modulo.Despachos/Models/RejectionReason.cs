namespace Modulo.Despachos.Models;

public class RejectionReason : AuditedEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string AppliesTo { get; set; } = RejectionAppliesTo.Rejection;
    public bool IsActive { get; set; } = true;
}
