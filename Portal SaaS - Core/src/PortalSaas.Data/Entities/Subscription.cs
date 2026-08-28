namespace PortalSaas.Data.Entities;

/// <summary>Estado comercial vigente de una Organization en modo SaaS.</summary>
public sealed class Subscription
{
    public long Id { get; set; }

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public long PlanId { get; set; }
    public Plan Plan { get; set; } = null!;

    /// <summary>"trial" | "active" | "past_due" | "cancelled" -- ver SubscriptionStatus.</summary>
    public string Status { get; set; } = null!;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>"stripe" | "flow" | "transbank" | null (on-premise).</summary>
    public string? PaymentProvider { get; set; }

    /// <summary>Id de cliente en el proveedor de pago -- NUNCA datos de tarjeta acá.</summary>
    public string? ExternalPaymentReference { get; set; }
}

public static class SubscriptionStatus
{
    public const string Trial = "trial";
    public const string Active = "active";
    public const string PastDue = "past_due";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyCollection<string> All = [Trial, Active, PastDue, Cancelled];
}
