using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de condiciones de pago (OCTG) de la compañía SAP activa -- tab Finanzas.</summary>
public interface IPaymentTermsCatalogService
{
    Task<IReadOnlyList<PaymentTermsDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
