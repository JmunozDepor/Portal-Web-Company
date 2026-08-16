using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.Integracion;

public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly RendicionesDbContext _db;
    private readonly ICatalogSyncProvider _sync;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(RendicionesDbContext db, ICatalogSyncProvider sync, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _sync = sync;
        _currentCompany = currentCompany;
    }

    public bool SapCatalogSyncEnabled { get; private set; }
    public DateTimeOffset? SettingsUpdatedAt { get; private set; }
    public IReadOnlyList<CostCenter> CostCenters { get; private set; } = Array.Empty<CostCenter>();
    public IReadOnlyList<GlAccount> GlAccounts { get; private set; } = Array.Empty<GlAccount>();

    [BindProperty]
    public CatalogEntryInput NewCostCenter { get; set; } = new();

    [BindProperty]
    public CatalogEntryInput NewGlAccount { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostToggleAsync(bool enabled, CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings is null)
        {
            settings = new RendicionesSettings { CompanyId = _currentCompany.CompanyId };
            _db.RendicionesSettings.Add(settings);
        }
        settings.SapCatalogSyncEnabled = enabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = enabled ? "Sincronización con SAP activada." : "Sincronización con SAP desactivada. Los catálogos ahora se editan a mano.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSincronizarAsync(CancellationToken ct)
    {
        try
        {
            var costCenterResult = await _sync.SyncCostCentersAsync(_currentCompany.CompanyId, ct);
            var glAccountResult = await _sync.SyncGlAccountsAsync(_currentCompany.CompanyId, ct);
            SuccessMessage = $"Sincronizado. Centros de costo: {costCenterResult.Created} nuevos, {costCenterResult.Updated} actualizados, {costCenterResult.Deactivated} desactivados. " +
                              $"Cuentas contables: {glAccountResult.Created} nuevas, {glAccountResult.Updated} actualizadas, {glAccountResult.Deactivated} desactivadas.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAgregarCentroCostoAsync(CancellationToken ct)
    {
        _db.CostCenters.Add(new CostCenter
        {
            CompanyId = _currentCompany.CompanyId,
            Code = NewCostCenter.Code,
            Name = NewCostCenter.Name,
            Source = CatalogEntrySource.Manual,
        });
        await _db.SaveChangesAsync(ct);
        SuccessMessage = "Centro de costo agregado.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAgregarCuentaAsync(CancellationToken ct)
    {
        _db.GlAccounts.Add(new GlAccount
        {
            CompanyId = _currentCompany.CompanyId,
            Code = NewGlAccount.Code,
            Name = NewGlAccount.Name,
            Source = CatalogEntrySource.Manual,
        });
        await _db.SaveChangesAsync(ct);
        SuccessMessage = "Cuenta contable agregada.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDesactivarCentroCostoAsync(long id, CancellationToken ct)
    {
        var row = await _db.CostCenters.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct);
        if (row is not null)
        {
            row.IsActive = !row.IsActive;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDesactivarCuentaAsync(long id, CancellationToken ct)
    {
        var row = await _db.GlAccounts.FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == _currentCompany.CompanyId, ct);
        if (row is not null)
        {
            row.IsActive = !row.IsActive;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        SapCatalogSyncEnabled = settings?.SapCatalogSyncEnabled ?? true;
        SettingsUpdatedAt = settings?.UpdatedAt;

        CostCenters = await _db.CostCenters.Where(c => c.CompanyId == _currentCompany.CompanyId).OrderBy(c => c.Name).ToListAsync(ct);
        GlAccounts = await _db.GlAccounts.Where(g => g.CompanyId == _currentCompany.CompanyId).OrderBy(g => g.Name).ToListAsync(ct);
    }

    public sealed class CatalogEntryInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Ingresá el código.")]
        [System.ComponentModel.DataAnnotations.StringLength(50)]
        public string Code { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Ingresá el nombre.")]
        [System.ComponentModel.DataAnnotations.StringLength(200)]
        public string Name { get; set; } = "";
    }
}
