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

    /// <summary>"AzureMaps" | "AzureDocumentIntelligence" | "GoogleGeminiVision" -- ver ExternalServiceType.</summary>
    public required string ServiceType { get; set; }

    /// <summary>Nombre descriptivo para distinguir cuentas del mismo servicio (ej. "Azure Maps - Cuenta 1").</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Azure Document Intelligence: endpoint del recurso (requerido). Azure Maps: endpoint
    /// fijo global, puede quedar null. Google Gemini: opcional -- si viene, se usa como id
    /// del modelo (ej. "gemini-2.0-flash"); si queda null se usa el modelo por defecto de
    /// GeminiReceiptExtractorService.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>Cifrado AES-256-GCM antes de guardar (ISecretoCifradoService), mismo criterio write-only que Instance/Company del portal.</summary>
    public required string ApiKeyEncrypted { get; set; }

    /// <summary>
    /// Tope local de solicitudes para UN período de cuota (ver <see cref="QuotaPeriods"/>
    /// y <see cref="QuotaPeriods.ForServiceType"/>): por MES para Azure Maps / Azure
    /// Document Intelligence, por DÍA para Google Gemini (capa gratuita ~1500/día). El
    /// contador local es solo un guardrail de presupuesto -- el corte real lo sigue
    /// imponiendo el proveedor.
    /// </summary>
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

    /// <summary>
    /// OCR de comprobantes vía Google Gemini (Google AI Studio / Gemini API) -- alternativa
    /// gratuita a Azure Document Intelligence, cuota por solicitudes (no por páginas) y con
    /// tope DIARIO (~1500 solicitudes/día en la capa gratuita, ver GeminiReceiptExtractorService),
    /// a diferencia de los servicios de Azure que se miden por mes. Si hay cuentas de los DOS
    /// tipos configuradas, Azure Document Intelligence tiene prioridad y Gemini actúa de
    /// fallback cuando aquél agota su cupo (ver CompositeReceiptExtractorService).
    /// </summary>
    public const string GoogleGeminiVision = "GoogleGeminiVision";

    public static readonly IReadOnlyCollection<string> All = [AzureMaps, AzureDocumentIntelligence, GoogleGeminiVision];
}

/// <summary>
/// Período sobre el que se cuenta el cupo de un ExternalServiceProvider. No se guarda en
/// la tabla: se deriva del ServiceType (Gemini = diario, el resto = mensual) y se propaga
/// a IExternalServiceUsageService para saber en qué "balde" de external_service_usages
/// acumular (columna day = 0 para mensual, 1..31 para diario).
/// </summary>
public static class QuotaPeriods
{
    public const string Monthly = "Monthly";
    public const string Daily = "Daily";

    /// <summary>Gemini corta por día; Azure Maps y Azure Document Intelligence, por mes.</summary>
    public static string ForServiceType(string serviceType) =>
        serviceType == ExternalServiceType.GoogleGeminiVision ? Daily : Monthly;
}
