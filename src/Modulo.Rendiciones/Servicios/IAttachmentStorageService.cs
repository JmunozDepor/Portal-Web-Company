using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Guarda el comprobante (imagen/PDF) de un gasto. Implementación MVP sobre la misma
/// base propia del plugin (columna binaria) -- detrás de una interfaz para poder
/// cambiar a blob storage externo si el volumen lo justifica, sin tocar el dominio.
/// </summary>
public interface IAttachmentStorageService
{
    Task<long> SaveAsync(Guid companyId, Guid userId, string fileName, string mimeType, byte[] content, CancellationToken ct = default);

    Task<ExpenseReceipt?> GetAsync(long id, Guid companyId, CancellationToken ct = default);

    Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default);
}
