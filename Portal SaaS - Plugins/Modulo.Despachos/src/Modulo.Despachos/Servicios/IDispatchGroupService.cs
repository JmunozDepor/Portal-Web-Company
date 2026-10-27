using Modulo.Despachos.Models;

namespace Modulo.Despachos.Servicios;

/// <summary>Transferencias SAP libres hacia una bodega sucursal (candidatas a agrupación).</summary>
public sealed record BranchCandidates(BranchWarehouse Branch, IReadOnlyList<SapTransferInfo> Transfers);

/// <summary>DispatchDate = un día; From/To = rango de fechas de despacho (ambos extremos incluidos).</summary>
public sealed record DispatchGroupFilter(DateOnly? DispatchDate = null, string? WarehouseCode = null, string? Status = null,
    DateOnly? From = null, DateOnly? To = null, string Kind = DispatchGroupKinds.Transfer);

public sealed record DispatchGroupListItem(long Id, int Number, string Code, string WarehouseCode, string BranchName,
    DateOnly DispatchDate, int Sequence, string Status, int Transfers, decimal Units, Guid? ManifestId, int? ManifestNumber);

/// <summary>
/// Agrupaciones: bodega agrupa por sucursal y día de despacho las transferencias SAP pendientes; despacho escanea la
/// hoja en el manifiesto. Todo se acota a la compañía activa; las violaciones de reglas lanzan
/// InvalidOperationException con un mensaje apto para el usuario.
/// </summary>
public interface IDispatchGroupService
{
    /// <summary>Pendientes libres (ni en agrupación activa ni en manifiesto activo) a sucursales activas, por sucursal.</summary>
    Task<IReadOnlyList<BranchCandidates>> ListCandidatesAsync(DateOnly fromDate, string? warehouseCode = null);

    /// <summary>
    /// Una agrupación por sucursal con las DocEntry elegidas que sigan libres (grupo siguiente del día para esa
    /// sucursal); avisa por la campana. Lanza si no queda ninguna.
    /// </summary>
    Task<IReadOnlyList<DispatchGroup>> GenerateAsync(DateOnly dispatchDate, DateOnly fromDate, IReadOnlyCollection<int> docEntries);

    /// <summary>Agrupación con sus transferencias, o null.</summary>
    Task<DispatchGroup?> GetAsync(long id);

    /// <summary>Agrupación por su correlativo (el del código AT), con sus transferencias, o null.</summary>
    Task<DispatchGroup?> GetByNumberAsync(int number);

    Task<IReadOnlyList<DispatchGroupListItem>> ListAsync(DispatchGroupFilter filter);

    /// <summary>Agrega una transferencia por LPN a una agrupación armada; devuelve el mensaje de confirmación.</summary>
    Task<string> AddLpnAsync(long groupId, string lpn);

    /// <summary>Quita una transferencia de una agrupación armada.</summary>
    Task RemoveTransferAsync(long groupId, long transferId);

    /// <summary>Anula una agrupación armada; sus transferencias quedan libres.</summary>
    Task AnnulAsync(long groupId, string reason);
}
