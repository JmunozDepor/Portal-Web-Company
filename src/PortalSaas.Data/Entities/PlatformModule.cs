namespace PortalSaas.Data.Entities;

/// <summary>Catálogo comercial de módulos vendibles (equivalente a MODULO_ORIGEN/CodigoModulo de PortalSAP_v2, ahora con precio/inclusión por plan).</summary>
public sealed class PlatformModule
{
    public long Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    /// <summary>true = incluido en todo plan, no se vende suelto.</summary>
    public bool IsCore { get; set; }

    public ICollection<PlanModule> PlanModules { get; set; } = new List<PlanModule>();
    public ICollection<OrganizationModule> OrganizationModules { get; set; } = new List<OrganizationModule>();
}

/// <summary>Qué módulos incluye cada Plan (N:N).</summary>
public sealed class PlanModule
{
    public long PlanId { get; set; }
    public Plan Plan { get; set; } = null!;

    public long ModuleId { get; set; }
    public PlatformModule Module { get; set; } = null!;
}

/// <summary>Qué módulos tiene contratados una Organization más allá de su plan base (add-ons).</summary>
public sealed class OrganizationModule
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public long ModuleId { get; set; }
    public PlatformModule Module { get; set; } = null!;

    public DateTimeOffset ContractedAt { get; set; } = DateTimeOffset.UtcNow;
}
