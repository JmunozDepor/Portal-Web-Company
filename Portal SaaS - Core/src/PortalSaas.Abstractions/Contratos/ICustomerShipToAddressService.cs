using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Direcciones de despacho (CRD1, AdresType = 'S') de un cliente -- usado por
/// Modulo.ImportacionGenerica (regla CustomerBranchValid) para validar que la
/// sucursal informada en una fila importada corresponda a ese cliente, y por el
/// tab Logística de los documentos de venta genéricos ("Sucursal de destino").
/// </summary>
public interface ICustomerShipToAddressService
{
    /// <summary>Lista vacía si el cliente no tiene direcciones de despacho configuradas.</summary>
    Task<IReadOnlyList<string>> GetShipToAddressCodesAsync(string cardCode, CancellationToken ct = default);

    /// <summary>
    /// Código + dirección legible de cada dirección de despacho del cliente -- para
    /// poblar el combo "Sucursal de destino". Implementación por defecto (solo códigos)
    /// para no obligar a los dobles de test.
    /// </summary>
    async Task<IReadOnlyList<CustomerShipToAddressDto>> ListShipToAddressesAsync(string cardCode, CancellationToken ct = default) =>
        (await GetShipToAddressCodesAsync(cardCode, ct)).Select(code => new CustomerShipToAddressDto { Code = code }).ToList();
}
