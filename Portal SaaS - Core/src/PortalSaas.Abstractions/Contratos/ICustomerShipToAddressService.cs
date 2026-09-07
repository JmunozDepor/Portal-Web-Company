namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Direcciones de despacho (CRD1, AddressType = 'S') de un cliente -- usado por
/// Modulo.ImportacionGenerica (regla CustomerBranchValid) para validar que la
/// sucursal informada en una fila importada corresponda a ese cliente.
/// </summary>
public interface ICustomerShipToAddressService
{
    /// <summary>Lista vacía si el cliente no tiene direcciones de despacho configuradas.</summary>
    Task<IReadOnlyList<string>> GetShipToAddressCodesAsync(string cardCode, CancellationToken ct = default);
}
