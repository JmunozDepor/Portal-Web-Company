namespace PortalSaas.Data.Entities;

/// <summary>
/// El cliente que paga -- nivel nuevo por encima de <see cref="Company"/> (ver
/// docs/03-MODELO-CORE-COMERCIAL.md). Un mismo cliente (ej. Comercial Depor) puede
/// tener varias Company (DEPOR, DEPORQA) colgando de la misma Organization.
/// </summary>
public sealed class Organization
{
    // Generado en C#, no en la base (gen_random_uuid()/NEWID() difieren por motor) --
    // ver docs/02-ARQUITECTURA-BASE-DE-DATOS.md §7 (motor dual de la base propia).
    public Guid Id { get; set; } = Guid.NewGuid();

    public string LegalName { get; set; } = null!;

    /// <summary>
    /// Código corto y único, ej. "comercial-depor" -- usado para resolver a qué
    /// organización pertenece un login, porque username/email son únicos solo DENTRO
    /// de la organización, no global (ver Entities/User.cs). Mismo criterio que
    /// Company.Code. Formato: minúsculas, dígitos, guiones -- sin espacios ni
    /// mayúsculas, para poder usarse también como subdominio a futuro sin traducción.
    /// </summary>
    public string Slug { get; set; } = null!;

    public string? TaxId { get; set; }
    public string Country { get; set; } = null!;

    /// <summary>"saas" | "on_premise" -- ver OrganizationMode.</summary>
    public string Mode { get; set; } = OrganizationMode.Saas;

    /// <summary>"trial" | "active" | "suspended" | "cancelled" -- ver OrganizationStatus.</summary>
    public string Status { get; set; } = OrganizationStatus.Trial;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Company> Companies { get; set; } = new List<Company>();
    public ICollection<Instance> Instances { get; set; } = new List<Instance>();
    public ICollection<User> Users { get; set; } = new List<User>();
}

public static class OrganizationMode
{
    public const string Saas = "saas";
    public const string OnPremise = "on_premise";

    public static readonly IReadOnlyCollection<string> All = [Saas, OnPremise];
}

public static class OrganizationStatus
{
    public const string Trial = "trial";
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyCollection<string> All = [Trial, Active, Suspended, Cancelled];
}
