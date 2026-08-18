using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.Notificaciones;

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
    public NotificationSettingsInput Settings { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings is not null)
        {
            Settings.ReminderHour = settings.ReminderHour;
            Settings.ReminderEnabled = settings.ReminderEnabled;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings is null)
        {
            settings = new RendicionesSettings { CompanyId = _currentCompany.CompanyId };
            _db.RendicionesSettings.Add(settings);
        }

        settings.ReminderHour = Settings.ReminderHour;
        settings.ReminderEnabled = Settings.ReminderEnabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Configuración de notificaciones guardada.";
        return RedirectToPage();
    }

    public sealed class NotificationSettingsInput
    {
        [Required]
        public TimeOnly ReminderHour { get; set; } = new(8, 0);

        public bool ReminderEnabled { get; set; } = true;
    }
}
