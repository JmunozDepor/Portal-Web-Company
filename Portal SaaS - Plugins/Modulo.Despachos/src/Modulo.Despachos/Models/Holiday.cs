namespace Modulo.Despachos.Models;

public class Holiday : AuditedEntity
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}
