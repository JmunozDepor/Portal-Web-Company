namespace Modulo.Despachos.Servicios;

/// <summary>Resumen de una guía planificada (vista previa en borrador).</summary>
public sealed record PlannedGuideView(
    int Sequence, string ReceiverName, string Destination, int Documents, int Lines, decimal Quantity, decimal? WeightKg);

/// <summary>Vista previa de las guías. Enabled = la compañía emite guías al despachar.</summary>
public sealed record GuidePreview(
    bool Enabled, IReadOnlyList<PlannedGuideView> Guides, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>Resultado de emitir las guías pendientes de un manifiesto.</summary>
public sealed record GuideEmissionOutcome(bool AllIssued, int Issued, int Pending, IReadOnlyList<string> Errors);

/// <summary>
/// Guías de despacho electrónicas (DTE 52, Res. 154) de un manifiesto, emitidas por la pasarela DTE de la
/// plataforma con el proveedor de la compañía de la sesión. Despachos nunca arma XML ni conoce al proveedor.
/// </summary>
public interface ITransportGuideService
{
    /// <summary>Guías persistidas del manifiesto (vacío si aún no se planificaron), por secuencia.</summary>
    Task<IReadOnlyList<Models.TransportGuide>> ListAsync(Guid manifestId);

    /// <summary>La compañía tiene activado "emitir guías al despachar" (DespachosSetting.GuidesEnabled).</summary>
    Task<bool> IsEnabledAsync();

    /// <summary>
    /// Plan sin persistir: errores de configuración DTE, del planificador y de las reglas del formato por guía.
    /// En borrador usa los datos de transporte vigentes de los maestros (aún no hay snapshot).
    /// </summary>
    Task<GuidePreview> PreviewAsync(Guid manifestId);

    /// <summary>PDF borrador del proveedor (sin folio) de la guía <paramref name="sequence"/> de la vista previa.</summary>
    Task<byte[]> PreviewPdfAsync(Guid manifestId, int sequence);

    /// <summary>
    /// Manifiesto en "guías pendientes": planifica la primera vez (persistiendo el plan) y emite las guías no
    /// emitidas. Las en curso se reenvían con su mismo contenido (idempotente); las rechazadas se replanifican.
    /// </summary>
    Task<GuideEmissionOutcome> EmitAsync(Guid manifestId);

    /// <summary>PDF de una guía emitida, o de todas unidas en un solo archivo (sequence null).</summary>
    Task<byte[]> GetPdfAsync(Guid manifestId, int? sequence);

    /// <summary>Desiste de las guías no emitidas (libera folios reservados). Lanza si alguna ya está emitida.</summary>
    Task AbandonPendingAsync(Guid manifestId, string reason);
}
