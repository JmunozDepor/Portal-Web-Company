using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de monedas (OCRN) de la compañía SAP activa -- necesario para clientes/documentos multi-moneda.</summary>
public interface ICurrencyCatalogService
{
    Task<IReadOnlyList<CurrencyDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
