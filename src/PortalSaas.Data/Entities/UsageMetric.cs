namespace PortalSaas.Data.Entities;

/// <summary>Evento de consumo (usuarios activos, documentos creados, transacciones) -- base para IContractLimitService y para facturación por consumo si el modelo comercial lo requiere.</summary>
public sealed class UsageMetric
{
    public long Id { get; set; }

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>"active_user" | "document_created" | "sap_transaction" -- catálogo abierto, no un enum cerrado todavía.</summary>
    public string MetricName { get; set; } = null!;

    public decimal Value { get; set; } = 1;

    /// <summary>Formato "YYYYMM".</summary>
    public string Period { get; set; } = null!;

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
