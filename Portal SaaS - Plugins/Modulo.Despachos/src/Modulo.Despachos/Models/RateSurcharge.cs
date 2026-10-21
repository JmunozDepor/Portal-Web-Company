namespace Modulo.Despachos.Models;

/// <summary>Recargo configurable sobre el valor base. Se mapea en el DbContext (Task 6).</summary>
public class RateSurcharge : AuditedEntity
{
    /// <summary>Nulo = aplica a todos los transportistas.</summary>
    public long? CarrierId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public string ConditionType { get; set; } = "";
    public string? ConditionParametersJson { get; set; }
    /// <summary>fixed_amount | percent_of_base | fixed_per_extra_unit.</summary>
    public string EffectType { get; set; } = "fixed_amount";
    public decimal Value { get; set; }
}
