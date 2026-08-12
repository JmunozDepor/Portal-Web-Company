namespace Modulo.Rendiciones.Models;

/// <summary>
/// Una cuenta/credencial configurada para un servicio externo con capa gratuita
/// (Azure Maps o Azure Document Intelligence). Puede haber MÁS DE UNA por
/// (CompanyId, ServiceType) -- cuando la de mayor prioridad agota su cuota mensual,
/// el servicio pasa a la siguiente automáticamente (ver IExternalServiceProviderSelector)
/// en vez de quedar bloqueado hasta el próximo mes. Reemplaza el esquema anterior
/// (una sola clave por servicio, fija en appsettings/user-secrets) -- ahora es 100%
/// self-service desde Configuracion &gt; Proveedores, sin tocar configuración de
/// despliegue para agregar una cuenta nueva.
/// </summary>
public class ExternalServiceProvider
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    /// <summary>"AzureMaps" | "AzureDocumentIntelligence" -- ver ExternalServiceType.</summary>
    public required string ServiceType { get; set; }

    /// <summary>Nombre descriptivo para distinguir cuentas del mismo servicio (ej. "Azure Maps - Cuenta 1").</summary>
    public required string Name { get; set; }

    /// <summary>Requerido para Azure Document Intelligence (endpoint del recurso); Azure Maps usa un endpoint fijo global, puede quedar null.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Cifrado AES-256-GCM antes de guardar (ISecretoCifradoService), mismo criterio write-only que Instance/Company del portal.</summary>
    public required string ApiKeyEncrypted { get; set; }

    public required int MonthlyLimit { get; set; }

    /// <summary>Orden de intento -- menor se prueba primero. Empate se resuelve por Id.</summary>
    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ExternalServiceType
{
    public const string AzureMaps = "AzureMaps";
    public const string AzureDocumentIntelligence = "AzureDocumentIntelligence";

    public static readonly IReadOnlyCollection<string> All = [AzureMaps, AzureDocumentIntelligence];
}
