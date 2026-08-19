using Servicios.TipoCambioBancoCentral.Contratos;

namespace Servicios.TipoCambioBancoCentral.Configuracion;

/// <summary>
/// Shape de configuración de UNA compañía en appsettings.json, sección "Sociedades"
/// (array) -- una entrada por compañía, agregar una compañía nueva es agregar un objeto al
/// array, sin tocar código. <c>ServiceLayerSecreto</c> viene cifrado con
/// ISecretoCifradoService.
/// </summary>
public sealed class TipoCambioCompanySetting
{
    public required string CompanyCode { get; init; }

    public required string ServiceLayerUrl { get; init; }

    public required string ServiceLayerDb { get; init; }

    public required string ServiceLayerUsername { get; init; }

    public required string ServiceLayerSecreto { get; init; }

    public bool IsActive { get; init; } = true;

    public TipoCambioCompanyConfig ToCompanyConfig() => new()
    {
        CompanyCode = CompanyCode,
        ServiceLayerUrl = ServiceLayerUrl,
        ServiceLayerDb = ServiceLayerDb,
        ServiceLayerUsername = ServiceLayerUsername,
        ServiceLayerSecretCifrado = ServiceLayerSecreto
    };
}
