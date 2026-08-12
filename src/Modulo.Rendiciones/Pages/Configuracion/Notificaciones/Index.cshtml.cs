using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Pages.Configuracion.Notificaciones;

public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly RendicionesDbContext _db;

    public IndexModel(RendicionesDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public NotificationSettingsInput Settings { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
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

        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is null)
        {
            settings = new RendicionesSettings { Id = 1 };
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
