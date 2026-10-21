namespace Modulo.Despachos.Models;

public class ZoneCommune
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public long ZoneId { get; set; }
    public long CommuneId { get; set; }
}
