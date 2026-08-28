using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de grupos de artículo (OITB) de la compañía SAP activa -- prerrequisito del importador genérico.</summary>
public interface IItemGroupCatalogService
{
    Task<IReadOnlyList<ItemGroupDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
