namespace Modulo.Rendiciones.Models;

/// <summary>
/// Imagen/PDF del comprobante de un gasto. Portado de ComprobanteAdjunto
/// (PortalSAP_v2) -- interfaz de almacenamiento propia (`IAttachmentStorageService`,
/// fase siguiente) para poder cambiar a blob storage externo sin tocar el dominio.
/// </summary>
public class ExpenseReceipt
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required Guid UserId { get; set; }

    public required string FileName { get; set; }

    public required string MimeType { get; set; }

    public int SizeBytes { get; set; }

    public required byte[] Content { get; set; }

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
