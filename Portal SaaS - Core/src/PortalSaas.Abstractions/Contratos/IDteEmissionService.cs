using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Pasarela de emisión de documentos tributarios electrónicos. El proveedor (FACELE, BES, ...) y sus
/// credenciales se resuelven SIEMPRE por la compañía de la sesión (ICurrentCompanyAccessor); sin
/// compañía o sin configuración DTE lanza InvalidOperationException con un mensaje presentable.
/// Los módulos nunca arman el XML del proveedor ni preguntan por su nombre.
/// </summary>
public interface IDteEmissionService
{
    /// <summary>Estado de la configuración DTE de la compañía actual (no lanza por falta de configuración).</summary>
    Task<DteConfigurationStatus> GetConfigurationAsync(CancellationToken ct = default);

    /// <summary>Reglas del formato y de la Res. 154, sin red.</summary>
    Task<IReadOnlyList<DteValidationError>> ValidateAsync(DteDocument doc, CancellationToken ct = default);

    /// <summary>Validación + PDF borrador del proveedor (sin folio). Pdf null si hay errores.</summary>
    Task<DtePreviewResult> PreviewAsync(DteDocument doc, CancellationToken ct = default);

    /// <summary>
    /// Emite de forma idempotente. La misma EmissionKey devuelve la emisión existente; con otro contenido
    /// solo se acepta si la emisión anterior falló sin folio. Una caída del proveedor no lanza: el
    /// resultado vuelve en su estado intermedio con Error y se puede reintentar.
    /// </summary>
    Task<DteEmissionResult> EmitAsync(DteEmissionRequest request, CancellationToken ct = default);

    /// <summary>Emisión de la compañía actual, o null.</summary>
    Task<DteEmissionResult?> GetAsync(Guid emissionKey, CancellationToken ct = default);

    /// <summary>PDF de un documento emitido (estado issued).</summary>
    Task<byte[]> GetPdfAsync(Guid emissionKey, bool cedible, int copies, CancellationToken ct = default);

    /// <summary>
    /// Desiste de una emisión no emitida: libera el folio reservado o marca abandonada una pendiente.
    /// Lanza si ya está emitida.
    /// </summary>
    Task AbandonAsync(Guid emissionKey, string reason, CancellationToken ct = default);
}
