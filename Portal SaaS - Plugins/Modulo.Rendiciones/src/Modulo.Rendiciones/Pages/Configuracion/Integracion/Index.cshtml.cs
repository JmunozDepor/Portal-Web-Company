using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.Integracion;

public sealed class IndexModel : RendicionesAdminPageModelBase
{
    private readonly RendicionesDbContext _db;
    private readonly ICatalogSyncProvider _sync;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(RendicionesDbContext db, ICatalogSyncProvider sync, IRendicionesUserRoleService roles,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany, ILogger<IndexModel> logger)
        : base(roles, currentUser, currentCompany)
    {
        _db = db;
        _sync = sync;
        _currentCompany = currentCompany;
        _logger = logger;
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

    public async Task<IActionResult> OnPostToggleAsync(CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings is null)
        {
            settings = new RendicionesSettings { CompanyId = _currentCompany.CompanyId };
            _db.RendicionesSettings.Add(settings);
        }

        // Se alterna contra el estado REAL en base, no contra un valor que venga del
        // formulario -- una página cacheada/reenviada con el hidden viejo mandaba a
        // "desactivar" aunque el botón dijera "Activar".
        var enabled = !settings.SapCatalogSyncEnabled;
        settings.SapCatalogSyncEnabled = enabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (!enabled)
        {
            SuccessMessage = "Sincronización con SAP desactivada. Los catálogos ahora se editan a mano.";
            return RedirectToPage();
        }

        // Activar dispara una primera sincronización de una: el usuario espera ver los
        // catálogos poblados al prender el switch, no una pantalla vacía hasta apretar
        // "Sincronizar ahora" aparte.
        var synced = await RunSyncAsync(ct);
        SuccessMessage = synced is null
            ? "Sincronización con SAP activada."
            : "Sincronización con SAP activada. " + synced;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSincronizarAsync(CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (!(settings?.SapCatalogSyncEnabled ?? true))
        {
            ErrorMessage = "La sincronización con SAP está desactivada para esta compañía.";
            return RedirectToPage();
        }

        var synced = await RunSyncAsync(ct);
        if (synced is not null)
        {
            SuccessMessage = synced;
        }
        return RedirectToPage();
    }

    /// <summary>
    /// Corre la sincronización de ambos catálogos y devuelve el resumen para mostrar, o
    /// <c>null</c> si falló (en ese caso ya dejó <see cref="ErrorMessage"/> seteado y logueó).
    /// </summary>
    private async Task<string?> RunSyncAsync(CancellationToken ct)
    {
        try
        {
            var costCenterResult = await _sync.SyncCostCentersAsync(_currentCompany.CompanyId, ct);
            var glAccountResult = await _sync.SyncGlAccountsAsync(_currentCompany.CompanyId, ct);

            var allWarnings = costCenterResult.Warnings.Concat(glAccountResult.Warnings).ToList();
            if (allWarnings.Count > 0)
            {
                _logger.LogWarning("Sincronización SAP con advertencias para la compañía {CompanyId}: {Warnings}", _currentCompany.CompanyId, string.Join(" | ", allWarnings));
                WarningMessage = string.Join(" ", allWarnings);
            }

            return $"Sincronizado. Centros de costo: {costCenterResult.Created} nuevos, {costCenterResult.Updated} actualizados, {costCenterResult.Deactivated} desactivados. " +
                   $"Cuentas contables: {glAccountResult.Created} nuevas, {glAccountResult.Updated} actualizadas, {glAccountResult.Deactivated} desactivadas.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló la sincronización de catálogos SAP para la compañía {CompanyId}", _currentCompany.CompanyId);
            ErrorMessage = "No se pudo sincronizar con SAP. Intentá nuevamente más tarde o contactá a soporte.";
            return null;
        }
    }

    public async Task<IActionResult> OnPostAgregarCentroCostoAsync(CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings?.SapCatalogSyncEnabled ?? true)
        {
            ErrorMessage = "No se pueden agregar catálogos manuales mientras la sincronización con SAP está activa.";
            return RedirectToPage();
        }

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
        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings?.SapCatalogSyncEnabled ?? true)
        {
            ErrorMessage = "No se pueden agregar catálogos manuales mientras la sincronización con SAP está activa.";
            return RedirectToPage();
        }

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
        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings?.SapCatalogSyncEnabled ?? true)
        {
            ErrorMessage = "No se pueden editar catálogos manuales mientras la sincronización con SAP está activa.";
            return RedirectToPage();
        }

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
        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings?.SapCatalogSyncEnabled ?? true)
        {
            ErrorMessage = "No se pueden editar catálogos manuales mientras la sincronización con SAP está activa.";
            return RedirectToPage();
        }

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
