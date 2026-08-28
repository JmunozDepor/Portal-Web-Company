namespace PortalSaas.Data.Entities;

public sealed class CompanyModuleConnection
{
    public long Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>IModuloPortal.ModuleCode.</summary>
    public string ModuleCode { get; set; } = null!;

    /// <summary>Slot lógico dentro del módulo. Default = "Default".</summary>
    public string Purpose { get; set; } = "Default";

    public long ConnectionId { get; set; }
    public CompanyExternalConnection Connection { get; set; } = null!;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
