using Modulo.Despachos.Models;

namespace Modulo.Despachos.Servicios;

/// <summary>Entrega que no se puede agrupar (sin OC, con más de una OC u OC no numérica) y el motivo.</summary>
public sealed record BlockedDelivery(SapDeliveryInfo Delivery, string Reason);

/// <summary>Entregas libres de una OC del cliente (candidatas a una agrupación EG-).</summary>
public sealed record PoCandidates(long CustomerPo, DateOnly? OrderDate, IReadOnlyList<SapDeliveryInfo> Deliveries);

/// <summary>Entregas abiertas y libres del cliente, por OC, más las que no se pueden agrupar.</summary>
public sealed record DeliveryCandidates(string CustomerCode, string CustomerName, string? CustomerTaxId,
    IReadOnlyList<PoCandidates> Orders, IReadOnlyList<BlockedDelivery> Blocked);

/// <summary>Agrupar: cliente (RUT o código SAP), Entregas desde, fecha de despacho y condiciones de la guía.</summary>
public sealed record DeliveryGroupInput(string Customer, DateOnly FromDate, DateOnly DispatchDate,
    int TransferIndicator, int DispatchType, IReadOnlyCollection<int> DocEntries);

/// <summary>
/// Entregas con guía previa: agrupa por OC las Entregas SAP de un cliente (una agrupación EG- por OC, en la misma tabla
/// que Agrupar traslados); la hoja se escanea en el manifiesto y al despachar sale una guía valorizada por OC. La consulta
/// y la lista usan <see cref="IDispatchGroupService"/> (Kind = delivery). Las violaciones de reglas lanzan
/// InvalidOperationException con un mensaje apto para el usuario.
/// </summary>
public interface IDeliveryGroupService
{
    /// <summary>Entregas abiertas y libres del cliente desde la fecha, por OC; lanza si el RUT no existe en SAP.</summary>
    Task<DeliveryCandidates> ListCandidatesAsync(string customer, DateOnly fromDate);

    /// <summary>Una agrupación por OC con las DocEntry elegidas que sigan libres y agrupables.</summary>
    Task<IReadOnlyList<DispatchGroup>> GenerateAsync(DeliveryGroupInput input);

    /// <summary>Agrega una Entrega del mismo cliente y OC por su LPN a una agrupación armada; devuelve la confirmación.</summary>
    Task<string> AddLpnAsync(long groupId, string lpn);

    /// <summary>Quita una Entrega de una agrupación armada.</summary>
    Task RemoveDeliveryAsync(long groupId, long lineId);

    /// <summary>Cambia indicador de traslado (1–9) y tipo de despacho (1–3) de una agrupación armada.</summary>
    Task SetTermsAsync(long groupId, int transferIndicator, int dispatchType);

    /// <summary>Anula una agrupación armada; sus Entregas quedan libres.</summary>
    Task AnnulAsync(long groupId, string reason);
}
