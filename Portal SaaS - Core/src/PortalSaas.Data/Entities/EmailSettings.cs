namespace PortalSaas.Data.Entities;

/// <summary>
/// Configuración de envío de correo de una organización -- 1:1 con Organization,
/// separada de la fila de identidad (mismo criterio que UserPreference). Nueva en
/// este proyecto: no existía en PortalSAP_v2. Cada organización/instalación
/// configura su propio proveedor (típicamente su propio dominio corporativo), nunca
/// se comparte un buzón entre organizaciones.
/// </summary>
public sealed class EmailSettings
{
    /// <summary>Comparte PK con Organization (relación 1:1) -- no es un id propio.</summary>
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>"google_workspace" | "microsoft365" -- ver EmailProviderType.</summary>
    public string Provider { get; set; } = null!;

    /// <summary>La casilla desde la que se envía (ej. "no-reply@comercialdepor.cl").</summary>
    public string SenderEmail { get; set; } = null!;

    public string? SenderDisplayName { get; set; }

    /// <summary>
    /// JSON cifrado (AES-256-GCM, ver ISecretoCifradoService) con la forma que exige
    /// el proveedor -- Microsoft365: {"tenantId","clientId","clientSecret"};
    /// GoogleWorkspace: {"clientEmail","privateKeyPem"}. Nunca en texto plano.
    /// </summary>
    public string EncryptedProviderConfig { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}

public static class EmailProviderType
{
    public const string GoogleWorkspace = "google_workspace";
    public const string Microsoft365 = "microsoft365";

    public static readonly IReadOnlyCollection<string> All = [GoogleWorkspace, Microsoft365];
}
