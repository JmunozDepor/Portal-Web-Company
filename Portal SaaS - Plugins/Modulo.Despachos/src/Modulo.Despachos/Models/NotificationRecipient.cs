namespace Modulo.Despachos.Models;

/// <summary>Usuario del portal que recibe los avisos del módulo (por compañía).</summary>
public class NotificationRecipient : AuditedEntity
{
    public Guid UserId { get; set; }
    public bool IsActive { get; set; } = true;
}
