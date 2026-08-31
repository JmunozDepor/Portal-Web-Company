namespace Modulo.Rendiciones.Servicios;

/// <summary>Comprobante ya normalizado y listo para persistir.</summary>
public sealed record ProcessedReceipt(string FileName, string MimeType, byte[] Content);

/// <summary>
/// Normaliza el archivo de un comprobante: las imágenes se reorientan por EXIF, se
/// reescalan a un lado máximo razonable para leer una boleta y se recomprimen a
/// JPEG -- una foto de celular de 8 MB baja a ~300-600 KB. Los PDF y cualquier tipo
/// no reconocido se devuelven tal cual (esta etapa no toca PDF).
/// </summary>
public interface IReceiptImageProcessor
{
    /// <summary>
    /// Devuelve el archivo normalizado. Si mimeType no es una imagen raster soportada
    /// (jpeg/png/webp), devuelve fileName/mimeType/content sin cambios.
    /// maxLongEdgePx / jpegQuality en null usan los valores por defecto de la
    /// implementación; se pueden pasar para tunear una corrida puntual.
    /// </summary>
    Task<ProcessedReceipt> ProcessAsync(
        string fileName, string mimeType, byte[] content,
        int? maxLongEdgePx = null, int? jpegQuality = null, CancellationToken ct = default);
}
