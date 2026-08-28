using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de unidades de medida (OUOM) de la compañía SAP activa -- prerrequisito del importador genérico.</summary>
public interface IUnitOfMeasureCatalogService
{
    Task<IReadOnlyList<UnitOfMeasureDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
