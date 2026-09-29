using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.ConfiguracionCaptura;

/// <summary>
/// Formatos de código de barra que la PWA de captura acepta al escanear -- admin-only,
/// a diferencia de "Validar producto" que es un ajuste por equipo dentro de la propia
/// PWA (ver Mantenedor.tsx). Una fila por compañía en CaptureSettings; si nunca se
/// guardó, se muestran los tres formatos tildados (mismo default permisivo que usa
/// GetAjustesCapturaAsync para la PWA).
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var fila = await _db.CaptureSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == _currentCompany.CompanyId, ct);

        Input = new InputModel
        {
            AllowEan8 = fila?.AllowEan8 ?? true,
            AllowUpcA = fila?.AllowUpcA ?? true,
            AllowEan13 = fila?.AllowEan13 ?? true,
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var fila = await _db.CaptureSettings.FirstOrDefaultAsync(s => s.CompanyId == _currentCompany.CompanyId, ct);
        if (fila is null)
        {
            fila = new CaptureSettings { CompanyId = _currentCompany.CompanyId };
            _db.CaptureSettings.Add(fila);
        }

        fila.AllowEan8 = Input.AllowEan8;
        fila.AllowUpcA = Input.AllowUpcA;
        fila.AllowEan13 = Input.AllowEan13;
        fila.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Configuración de captura guardada.";
        return RedirectToPage();
    }

    public sealed class InputModel
    {
        public bool AllowEan8 { get; set; }
        public bool AllowUpcA { get; set; }
        public bool AllowEan13 { get; set; }
    }
}
