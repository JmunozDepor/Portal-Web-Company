using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;

namespace Modulo.Despachos.Servicios;

/// <summary>Filtro de la lista de guías (todas: del manifiesto y manuales). Receiver busca en nombre o RUT.</summary>
public sealed record GuideFilter(string? Source, string? Status, DateOnly? From, DateOnly? To, string? Receiver);

/// <summary>Fila de la lista de guías. Code = "GM-000123" (manual) o "Manifiesto N° 7 / guía 2".</summary>
public sealed record GuideListItem(
    Guid Id, string Source, string Code, Guid? ManifestId, int? ManifestNumber, long? Folio, DateOnly Date,
    string ReceiverName, string ReceiverTaxId, string DestinationCommune, int TransferIndicator, decimal? TotalAmount, string Status);

/// <summary>
/// Guías manuales (DTE 52 sin documento SAP): borrador GM-, motivos de traslado, emisión sola por la pasarela DTE y la
/// lista de todas las guías del módulo. La emisión dentro de un manifiesto la hace ITransportGuideService al despachar.
/// </summary>
public interface IManualGuideService
{
    /// <summary>Motivos de la compañía por código; la primera vez siembra 5 y 6 activos y 2, 4 y 7 inactivos.</summary>
    Task<IReadOnlyList<TransferMotive>> ListMotivesAsync(bool includeInactive = false);

    Task SaveMotiveAsync(TransferMotive motive);

    /// <summary>Crea el borrador (GM- correlativo). Lanza InvalidOperationException con los errores del formulario.</summary>
    Task<Guid> CreateAsync(ManualGuideInput input);

    /// <summary>Edita un borrador, una guía en manifiesto (transporte = del manifiesto) o una rechazada.</summary>
    Task UpdateAsync(Guid id, ManualGuideInput input);

    Task<TransportGuide?> GetAsync(Guid id);
    Task<TransportGuide?> GetByNumberAsync(int number);

    /// <summary>Formulario de la guía, para editarla o verla.</summary>
    Task<ManualGuideInput> GetInputAsync(Guid id);

    Task<IReadOnlyList<GuideListItem>> ListAsync(GuideFilter filter);

    /// <summary>Anula un borrador (no una guía en manifiesto ni emitida). Motivo obligatorio.</summary>
    Task AnnulAsync(Guid id, string reason);

    /// <summary>PDF borrador del proveedor (sin folio).</summary>
    Task<byte[]> PreviewPdfAsync(Guid id);

    /// <summary>
    /// Emite una guía sola (no en un manifiesto). Idempotente: la misma llave de emisión; una en curso o emitida en la
    /// pasarela se reconcilia sin reenviar.
    /// </summary>
    Task<TransportGuide> EmitAsync(Guid id);

    /// <summary>PDF de la guía emitida.</summary>
    Task<byte[]> GetPdfAsync(Guid id);

    /// <summary>Socio de negocio SAP por RUT, o null.</summary>
    Task<SapBusinessPartner?> FindReceiverAsync(string taxId);
}
