using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.Proveedores;

/// <summary>
/// CRUD de cuentas/credenciales para los servicios externos con capa gratuita (Azure
/// Maps/Azure Document Intelligence) -- puede haber más de una por servicio, con
/// fallback automático por prioridad (ver IExternalServiceProviderSelector). Clave
/// write-only, mismo patrón que Instance/Company del portal: nunca se vuelve a
/// mostrar una vez guardada, dejarla en blanco al editar no la cambia.
/// </summary>
public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly IExternalServiceProviderService _providers;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExternalServiceProviderService providers, ICurrentCompanyAccessor currentCompany)
    {
        _providers = providers;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<ExternalServiceProvider> Providers { get; private set; } = Array.Empty<ExternalServiceProvider>();

    public IReadOnlyList<string> ServiceTypes { get; } = ExternalServiceType.All.ToList();

    [BindProperty]
    public NewProviderInput New { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Providers = await _providers.ListAsync(_currentCompany.CompanyId, ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Providers = await _providers.ListAsync(_currentCompany.CompanyId, ct);
            return Page();
        }

        try
        {
            await _providers.CreateAsync(_currentCompany.CompanyId, New.ServiceType, New.Name, New.Endpoint, New.ApiKey, New.MonthlyLimit, New.Priority, ct);
            SuccessMessage = "Proveedor creado.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            Providers = await _providers.ListAsync(_currentCompany.CompanyId, ct);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostGuardarAsync(long id, string nombre, string? endpoint, string? apiKey, int monthlyLimit, int priority, bool activo, CancellationToken ct)
    {
        try
        {
            await _providers.UpdateAsync(id, _currentCompany.CompanyId, nombre, endpoint, apiKey, monthlyLimit, priority, activo, ct);
            SuccessMessage = "Proveedor actualizado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        await _providers.DeleteAsync(id, _currentCompany.CompanyId, ct);
        SuccessMessage = "Proveedor eliminado.";
        return RedirectToPage();
    }

    public sealed class NewProviderInput
    {
        [Required(ErrorMessage = "Elegí un tipo de servicio.")]
        public string ServiceType { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Endpoint { get; set; }

        [Required(ErrorMessage = "Ingresá la clave del proveedor.")]
        [StringLength(500)]
        public string ApiKey { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "El límite mensual debe ser mayor a 0.")]
        public int MonthlyLimit { get; set; }

        public int Priority { get; set; }
    }
}
