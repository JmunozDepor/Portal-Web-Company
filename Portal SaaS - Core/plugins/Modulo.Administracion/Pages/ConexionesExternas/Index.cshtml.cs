// plugins/Modulo.Administracion/Pages/ConexionesExternas/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.ConexionesExternas;

/// <summary>
/// Self-service del admin de un tenant: base de datos externa de cada módulo (instancia +
/// base + credencial propia opcional) en las compañías de la PROPIA organización
/// (ICurrentUserContext.OrganizationId). Las instancias las administra la plataforma; acá
/// solo se eligen. Selector de compañía de solo lectura (nunca recibe organizationId de la UI).
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
    public IReadOnlyList<ModuleConnectionBindingDto> Bindings { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid? companyId)
    {
        Companies = await _tenant.ListCompaniesAsync();
        if (Companies.Count == 0) return Page();

        CompanyId = companyId ?? Companies[0].Id;
        if (!Companies.Any(c => c.Id == CompanyId)) { MensajeError = "Compañía inválida."; CompanyId = Companies[0].Id; }

        Bindings = await _svc.ListBindingsAsync(CurrentUser.OrganizationId, CompanyId.Value);
        return Page();
    }

    private async Task<bool> EsCompaniaValidaAsync(Guid companyId)
        => (await _tenant.ListCompaniesAsync()).Any(c => c.Id == companyId);

    public async Task<IActionResult> OnPostTestAsync(Guid companyId, string moduleCode)
    {
        if (!await EsCompaniaValidaAsync(companyId)) return Forbid();
        try
        {
            var r = await _svc.TestBindingAsync(CurrentUser.OrganizationId, companyId, moduleCode);
            MensajeExito = r.Ok ? $"Conexión OK ({r.ElapsedMs} ms)." : null;
            MensajeError = r.Ok ? null : $"Falló la conexión: {r.Error}";
        }
        catch (Exception ex) { MensajeError = ObtenerMensajeError(ex); }
        return RedirectToPage(new { companyId });
    }

    public async Task<IActionResult> OnPostClearAsync(Guid companyId, string moduleCode)
    {
        if (!await EsCompaniaValidaAsync(companyId)) return Forbid();
        try { await _svc.ClearBindingAsync(CurrentUser.OrganizationId, companyId, moduleCode); MensajeExito = "Base quitada."; }
        catch (Exception ex) { MensajeError = ObtenerMensajeError(ex); }
        return RedirectToPage(new { companyId });
    }
}
