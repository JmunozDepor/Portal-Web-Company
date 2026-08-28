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

    [BindProperty] public ExternalConnectionEditModel Input { get; set; } = new();
    [BindProperty] public long? Id { get; set; }
    [BindProperty(SupportsGet = true)] public Guid CompanyId { get; set; }
    public bool EsNuevo => Id is null;
    public IReadOnlyList<string> Tipos => ExternalConnectionType.All.ToList();

    public async Task<IActionResult> OnGetAsync(long? id)
    {
        await ValidarCompaniaAsync();
        if (id is not null)
        {
            var dto = await _svc.GetAsync(CurrentUser.OrganizationId, CompanyId, id.Value);
            if (dto is null) return NotFound();
            Id = dto.Id;
            Input = new ExternalConnectionEditModel
            {
                Nombre = dto.Nombre, Tipo = dto.Tipo, Host = dto.Host, BaseUrl = dto.BaseUrl,
                Port = dto.Port, DatabaseName = dto.DatabaseName, TechnicalUsername = dto.TechnicalUsername,
                ConfiguracionExtra = dto.ConfiguracionExtra, IsActive = dto.IsActive,
            };
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await ValidarCompaniaAsync();
        if (!ModelState.IsValid) return Page();
        try
        {
            if (Id is null)
                await _svc.CreateAsync(CurrentUser.OrganizationId, CompanyId, Input);
            else
                await _svc.UpdateAsync(CurrentUser.OrganizationId, CompanyId, Id.Value, Input);
            MensajeExito = "Conexión guardada.";
            return RedirectToPage("Index", new { companyId = CompanyId });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    private async Task ValidarCompaniaAsync()
    {
        var companies = await _tenant.ListCompaniesAsync();
        if (!companies.Any(c => c.Id == CompanyId))
            throw new InvalidOperationException("Compañía inválida para esta organización.");
    }
}
