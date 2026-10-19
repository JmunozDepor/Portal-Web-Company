using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de monedas (OCRN) de la compañía SAP activa -- necesario para clientes/documentos multi-moneda.</summary>
public interface ICurrencyCatalogService
{
    Task<IReadOnlyList<CurrencyDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// Moneda local de la compañía (OADM.MainCurncy) -- moneda por defecto de un documento
    /// cuando el socio no tiene una moneda fija (OCRD.Currency vacío o "##" = todas las
    /// monedas). Implementación por defecto null, para los dobles de test.
    /// </summary>
    Task<string?> GetLocalCurrencyAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
}
