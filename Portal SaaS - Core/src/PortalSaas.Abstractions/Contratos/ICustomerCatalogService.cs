using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de clientes (OCRD, CardType='C') de la compañía SAP activa.</summary>
public interface ICustomerCatalogService
{
    Task<IReadOnlyList<CustomerDto>> ListAsync(CustomerFilter? filter = null, CancellationToken ct = default);

    Task<CustomerDto?> GetAsync(string cardCode, CancellationToken ct = default);

    /// <summary>
    /// CardCode -> validFor == "Y" en OCRD, para varios códigos en una sola consulta --
    /// usado por Modulo.ImportacionGenerica (regla CustomerActiveInSap). Un CardCode que
    /// no existe simplemente no aparece en el resultado.
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default);
}
