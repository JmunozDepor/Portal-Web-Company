using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Resuelve los valores por defecto que SAP ya tiene configurados para un socio de
/// negocio (OCRD.SlpCode/ListNum) -- lectura directa a HANA, sin pasar por Service
/// Layer (mismo criterio que ICustomerCatalogService.GetAsync). Portado de
/// ISocioNegocioDefaultsService en referencia-original/PortalSAP_v2.
/// </summary>
public interface IBusinessPartnerDefaultsService
{
    Task<BusinessPartnerDefaultsDto?> GetAsync(string cardCode, CancellationToken ct = default);
}
