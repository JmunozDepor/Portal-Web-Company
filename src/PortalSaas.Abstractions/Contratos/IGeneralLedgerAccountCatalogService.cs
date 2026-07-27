using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de cuentas contables (OACT) de la compañía SAP activa -- usado por las líneas de tipo Servicio de Venta/Compra.</summary>
public interface IGeneralLedgerAccountCatalogService
{
    Task<IReadOnlyList<GeneralLedgerAccountDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
