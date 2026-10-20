using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.Formato;

/// <summary>Un solo toggle: con/sin decimales en los montos de todo el módulo (ver IAmountFormatService / &lt;rnd-amount&gt;).</summary>
public sealed class IndexModel : RendicionesAdminPageModelBase
{
    private readonly RendicionesDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(RendicionesDbContext db, IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    [BindProperty]
    public FormatSettingsInput Settings { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings is not null)
            Settings.ShowAmountDecimals = settings.ShowAmountDecimals;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings is null)
        {
            settings = new RendicionesSettings { CompanyId = _currentCompany.CompanyId };
            _db.RendicionesSettings.Add(settings);
        }

        settings.ShowAmountDecimals = Settings.ShowAmountDecimals;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Formato de montos guardado.";
        return RedirectToPage();
    }

    public sealed class FormatSettingsInput
    {
        [Display(Name = "Mostrar decimales en los montos")]
        public bool ShowAmountDecimals { get; set; }
    }
}
