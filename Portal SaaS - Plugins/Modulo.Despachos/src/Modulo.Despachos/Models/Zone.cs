namespace Modulo.Despachos.Models;

public class Zone : AuditedEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    /// <summary>Comunas de la zona (N:M); SaveZoneAsync sincroniza esta lista con la tabla.</summary>
    public List<ZoneCommune> Communes { get; set; } = new();
}
