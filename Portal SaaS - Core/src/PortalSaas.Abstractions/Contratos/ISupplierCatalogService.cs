using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de proveedores (OCRD, CardType='S') de la compañía SAP activa -- lado proveedor de ICustomerCatalogService.</summary>
public interface ISupplierCatalogService
{
    Task<IReadOnlyList<SupplierDto>> ListAsync(SupplierFilter? filter = null, CancellationToken ct = default);

    Task<SupplierDto?> GetAsync(string cardCode, CancellationToken ct = default);

    /// <summary>
    /// CardCode -> validFor == "Y" en OCRD (CardType='S'), para varios códigos en una
    /// sola consulta -- usado por Modulo.ImportacionGenerica (regla CustomerActiveInSap
    /// en Compra). Un CardCode que no existe simplemente no aparece en el resultado.
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default);
}
