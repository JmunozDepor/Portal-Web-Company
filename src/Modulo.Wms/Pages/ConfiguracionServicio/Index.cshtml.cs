using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Wms.Pages.ConfiguracionServicio;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IServiceConfigService _serviceConfigs;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;

    public IndexModel(IServiceConfigService serviceConfigs, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser)
    {
        _serviceConfigs = serviceConfigs;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
    }

    public IReadOnlyList<WmsServiceConfig> Configs { get; private set; } = Array.Empty<WmsServiceConfig>();

    public IReadOnlyDictionary<string, (string Label, string Hint)> ConfigKeyInfo => WmsServiceConfigKeys.Labels;

    [BindProperty]
    public NewConfigInput New { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Configs = await _serviceConfigs.ListAllAsync(_currentCompany.CompanyId, ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Configs = await _serviceConfigs.ListAllAsync(_currentCompany.CompanyId, ct);
            return Page();
        }

        try
        {
            await _serviceConfigs.CreateAsync(_currentCompany.CompanyId, New.ConfigKey, New.ConfigValue, New.IsActive, _currentUser.Username, ct);
            SuccessMessage = "Parámetro creado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long id, string? configValue, bool activo, CancellationToken ct) =>
        await UpdateAsync(id, configValue, activo, ct);

    public async Task<IActionResult> OnPostDesactivarAsync(long id, string? configValue, CancellationToken ct) =>
        await UpdateAsync(id, configValue, activo: false, ct);

    public async Task<IActionResult> OnPostActivarAsync(long id, string? configValue, CancellationToken ct) =>
        await UpdateAsync(id, configValue, activo: true, ct);

    private async Task<IActionResult> UpdateAsync(long id, string? configValue, bool activo, CancellationToken ct)
    {
        try
        {
            await _serviceConfigs.UpdateAsync(id, _currentCompany.CompanyId, configValue, activo, _currentUser.Username, ct);
            SuccessMessage = "Parámetro actualizado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public sealed class NewConfigInput
    {
        [Required]
        public string ConfigKey { get; set; } = string.Empty;

        [StringLength(500)]
        public string? ConfigValue { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
