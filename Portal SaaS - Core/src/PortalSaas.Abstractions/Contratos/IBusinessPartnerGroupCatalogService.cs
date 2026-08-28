using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de grupos de socio de negocio (OCRG) de la compañía SAP activa -- prerrequisito del importador genérico.</summary>
public interface IBusinessPartnerGroupCatalogService
{
    Task<IReadOnlyList<BusinessPartnerGroupDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
