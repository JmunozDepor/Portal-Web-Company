using Microsoft.Extensions.Options;
using Servicios.TipoCambioBancoCentral.Contratos;

namespace Servicios.TipoCambioBancoCentral.Configuracion;

/// <summary>
/// Implementación de hoy de ITipoCambioCompanyProvider: lee la lista de compañías directo
/// de appsettings.json (sección "Sociedades"). Sirve igual para 1 compañía (modo standalone
/// del servicio legado) que para N.
/// </summary>
public sealed class AppSettingsTipoCambioCompanyProvider : ITipoCambioCompanyProvider
{
    private readonly IOptions<List<TipoCambioCompanySetting>> _sociedades;

    public AppSettingsTipoCambioCompanyProvider(IOptions<List<TipoCambioCompanySetting>> sociedades)
    {
        _sociedades = sociedades;
    }

    public Task<IReadOnlyList<TipoCambioCompanyConfig>> GetActiveCompaniesAsync(CancellationToken ct)
    {
        var resultado = _sociedades.Value
            .Where(s => s.IsActive)
            .Select(s => s.ToCompanyConfig())
            .ToList();

        return Task.FromResult<IReadOnlyList<TipoCambioCompanyConfig>>(resultado);
    }
}
