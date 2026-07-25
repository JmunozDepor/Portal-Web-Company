namespace PortalSaas.Data.Entities;

/// <summary>Catálogo de planes/tiers comerciales (ver docs/04-MODELO-COMERCIAL-NEGOCIO.md).</summary>
public sealed class Plan
{
    public long Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    /// <summary>Null = ilimitado.</summary>
    public int? UserLimit { get; set; }
    public int? CompanyLimit { get; set; }
    public int? MonthlyTransactionLimit { get; set; }

    public decimal? MonthlyPrice { get; set; }
    public string Currency { get; set; } = "CLP";
    public bool IsActive { get; set; } = true;

    public ICollection<PlanModule> PlanModules { get; set; } = new List<PlanModule>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<OnPremiseLicense> OnPremiseLicenses { get; set; } = new List<OnPremiseLicense>();
}
