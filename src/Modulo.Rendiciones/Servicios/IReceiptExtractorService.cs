namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Lee un comprobante (PDF o foto) y extrae los campos que puede -- detrás de una
/// interfaz para poder cambiar de proveedor sin tocar el resto del dominio (mismo
/// criterio que IAttachmentStorageService). Implementación actual: Azure Document
/// Intelligence (modelo prebuilt-invoice), plan gratuito hasta 500 documentos/mes.
/// </summary>
public interface IReceiptExtractorService
{
    /// <summary>Nunca lanza por un documento que el proveedor no pudo leer -- devuelve el DTO con Error seteado, para que un lote de varios archivos no se caiga entero por uno malo.</summary>
    Task<ExtractedReceiptDto> ExtractAsync(Guid companyId, byte[] content, string mimeType, CancellationToken ct = default);
}
