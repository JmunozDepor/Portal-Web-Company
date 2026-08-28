// plugins/Modulo.Administracion/Pages/ConexionesExternas/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.ConexionesExternas;

/// <summary>
/// Self-service del admin de un tenant: catálogo de conexiones externas + bindings
/// módulo→conexión de las compañías de la PROPIA organización
/// (ICurrentUserContext.OrganizationId). Equivalente tenant de las páginas /Admin,
/// con un selector de compañía de solo lectura (nunca recibe organizationId de la UI).
/// </summary>
public sealed class IndexModel : AdminPageModelBase
{
    private readonly ICompanyExternalConnectionService _svc;
    private readonly ITenantUserAdminService _tenant;

    public IndexModel(ICompanyExternalConnectionService svc, ITenantUserAdminService tenant, ICurrentUserContext currentUser)
        : base(currentUser)
    {
        _svc = svc;
        _tenant = tenant;
    }

    public IReadOnlyList<CompanyOptionDto> Companies { get; private set; } = [];
    public Guid? CompanyId { get; private set; }
    public IReadOnlyList<ExternalConnectionDto> Connections { get; private set; } = [];
    public IReadOnlyList<ModuleConnectionBindingDto> Bindings { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid? companyId)
    {
        Companies = await _tenant.ListCompaniesAsync();
        if (Companies.Count == 0) return Page();

        CompanyId = companyId ?? Companies[0].Id;
        if (!Companies.Any(c => c.Id == CompanyId)) { MensajeError = "Compañía inválida."; CompanyId = Companies[0].Id; }

        var orgId = CurrentUser.OrganizationId;
        Connections = await _svc.ListAsync(orgId, CompanyId.Value);
        Bindings = await _svc.ListBindingsAsync(orgId, CompanyId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync(Guid companyId, long id)
    {
        try
        {
            var r = await _svc.TestAsync(CurrentUser.OrganizationId, companyId, id);
            MensajeExito = r.Ok ? $"Conexión OK ({r.ElapsedMs} ms)." : null;
            MensajeError = r.Ok ? null : $"Falló la conexión: {r.Error}";
        }
        catch (Exception ex) { MensajeError = ObtenerMensajeError(ex); }
        return RedirectToPage(new { companyId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid companyId, long id)
    {
        try { await _svc.DeleteAsync(CurrentUser.OrganizationId, companyId, id); MensajeExito = "Conexión eliminada."; }
        catch (Exception ex) { MensajeError = ObtenerMensajeError(ex); }
        return RedirectToPage(new { companyId });
    }

    public async Task<IActionResult> OnPostSetBindingAsync(Guid companyId, string moduleCode, string purpose, long? connectionId)
    {
        try
        {
            if (connectionId is null or 0)
                await _svc.ClearBindingAsync(CurrentUser.OrganizationId, companyId, moduleCode, purpose);
            else
                await _svc.SetBindingAsync(CurrentUser.OrganizationId, companyId, moduleCode, purpose, connectionId.Value);
            MensajeExito = "Asignación actualizada.";
        }
        catch (Exception ex) { MensajeError = ObtenerMensajeError(ex); }
        return RedirectToPage(new { companyId });
    }
}
