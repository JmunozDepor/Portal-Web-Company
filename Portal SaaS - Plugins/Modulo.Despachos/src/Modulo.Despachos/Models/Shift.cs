namespace Modulo.Despachos.Models;

public class Shift : AuditedEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
