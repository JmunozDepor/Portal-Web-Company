using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica.Pages.Trabajos;

/// <summary>Detalle de un trabajo de importación: avance en vivo y reporte final por OC (mismo reporte que Importar, ver importacion-resultado.js).</summary>
public sealed class ImportacionTrabajoDetalleModel : PageModelBaseAdmin
{
    private readonly IGenericImportJobService _jobs;

    public ImportacionTrabajoDetalleModel(ICurrentUserContext currentUser, IGenericImportJobService jobs) : base(currentUser)
    {
        _jobs = jobs;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public GenericImportJobDto? Job { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Job = await _jobs.GetAsync(Id, ct);
        return Job is null ? RedirectToPage("/Trabajos/ImportacionTrabajos") : Page();
    }

    public async Task<JsonResult> OnGetProgressAsync(CancellationToken ct) => new(await _jobs.GetProgressAsync(Id, ct));
}
