namespace Modulo.Despachos.Models;

/// <summary>Base de los maestros: llave, compañía y auditoría (fechas siempre UTC).</summary>
public abstract class AuditedEntity
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
