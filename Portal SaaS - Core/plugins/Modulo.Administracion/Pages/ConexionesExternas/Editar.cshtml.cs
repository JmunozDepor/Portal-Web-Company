// plugins/Modulo.Administracion/Pages/ConexionesExternas/Editar.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.ConexionesExternas;

public sealed class EditarModel : AdminPageModelBase
{
    private readonly ICompanyExternalConnectionService _svc;
    private readonly ITenantUserAdminService _tenant;

    public EditarModel(ICompanyExternalConnectionService svc, ITenantUserAdminService tenant, ICurrentUserContext currentUser)
        : base(currentUser) { _svc = svc; _tenant = tenant; }

    [BindProperty] public ModuleDatabaseBindingInput Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public Guid CompanyId { get; set; }
    public ModuleConnectionBindingDto Binding { get; private set; } = null!;
    public IReadOnlyList<DatabaseInstanceOptionDto> Instances { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string moduleCode)
    {
        if (!await EsCompaniaValidaAsync()) return RedirectToPage("Index");
        if (!await CargarAsync(moduleCode)) return NotFound();

        Input = new ModuleDatabaseBindingInput
        {
            InstanceId = Binding.InstanceId ?? 0,
            DatabaseName = Binding.DatabaseName ?? string.Empty,
            UseOwnCredentials = Binding.HasOwnCredentials,
            TechnicalUsername = Binding.OwnTechnicalUsername,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string moduleCode)
    {
        if (!await EsCompaniaValidaAsync()) return Forbid();
        if (!await CargarAsync(moduleCode)) return NotFound();
        if (!ModelState.IsValid) return Page();
        try
        {
            await _svc.SetBindingAsync(CurrentUser.OrganizationId, CompanyId, moduleCode, Input);
            MensajeExito = "Base de datos guardada.";
            return RedirectToPage("Index", new { companyId = CompanyId });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    private async Task<bool> CargarAsync(string moduleCode)
    {
        var binding = await _svc.GetBindingAsync(CurrentUser.OrganizationId, CompanyId, moduleCode);
        if (binding is null) return false;
        Binding = binding;
        Instances = await _svc.ListInstancesAsync(CurrentUser.OrganizationId, CompanyId);
        return true;
    }

    private async Task<bool> EsCompaniaValidaAsync()
        => (await _tenant.ListCompaniesAsync()).Any(c => c.Id == CompanyId);
}
