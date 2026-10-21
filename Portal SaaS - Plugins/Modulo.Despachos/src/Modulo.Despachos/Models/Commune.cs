namespace Modulo.Despachos.Models;

/// <summary>Comuna del catálogo oficial (código CUT). El DTE usa el nombre (máx. 20 caracteres).</summary>
public class Commune : AuditedEntity
{
    public string CutCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string RegionCode { get; set; } = "";
    public string RegionName { get; set; } = "";
}
