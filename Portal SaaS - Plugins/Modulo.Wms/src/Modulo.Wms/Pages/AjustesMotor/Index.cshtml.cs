using Microsoft.AspNetCore.Mvc;
using Modulo.Wms.Services;

namespace Modulo.Wms.Pages.AjustesMotor;

/// <summary>
/// Mantención de wms_runtime_settings -- umbrales de reintento de los background
/// services, editables sin redeploy. Whitelist fija (WmsRuntimeSettingsKeys.Todas):
/// no hay alta libre, solo editar un valor o restaurar su default de código.
/// Nivel de ENTORNO (una fila por BD de módulo, sin company_id).
/// </summary>
public sealed class IndexModel : WmsPageModelBase
{
    private readonly IWmsRuntimeSettingsService _settings;
    private readonly PortalSaas.Abstractions.Contratos.ICurrentUserContext _currentUser;

    public IndexModel(IWmsRuntimeSettingsService settings, PortalSaas.Abstractions.Contratos.ICurrentUserContext currentUser)
    {
        _settings = settings;
        _currentUser = currentUser;
    }

    public IReadOnlyList<(WmsRuntimeSettingKey Meta, int ValorEfectivo, bool EsDefault)> Ajustes { get; private set; } =
        Array.Empty<(WmsRuntimeSettingKey, int, bool)>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Ajustes = await _settings.ListarAsync(ct);
    }

    public async Task<IActionResult> OnPostGuardarAsync(string clave, string valor, CancellationToken ct)
    {
        try
        {
            await _settings.GuardarAsync(clave, valor, _currentUser.Username, ct);
            SuccessMessage = "Ajuste guardado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestaurarAsync(string clave, CancellationToken ct)
    {
        try
        {
            await _settings.RestaurarDefaultAsync(clave, ct);
            SuccessMessage = "Ajuste restaurado al valor por defecto.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }
}
