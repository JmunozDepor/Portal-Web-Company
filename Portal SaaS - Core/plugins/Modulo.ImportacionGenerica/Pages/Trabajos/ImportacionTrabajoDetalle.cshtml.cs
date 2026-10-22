using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica.Pages.Trabajos;

/// <summary>
/// Detalle de un trabajo de importación: avance en vivo y reporte final por OC (mismo
/// reporte que Importar, ver importacion-resultado.js). Si es una carga guardada sin enviar
/// (borradores etapa 2): Revalidar (abre Importar con ?job=, vista previa recalculada
/// contra SAP), Enviar a SAP y Descartar -- cualquier usuario con permiso.
/// </summary>
public sealed class ImportacionTrabajoDetalleModel : PageModelBaseAdmin
{
    private readonly IGenericImportJobService _jobs;
    private readonly ILogger<ImportacionTrabajoDetalleModel> _logger;

    public ImportacionTrabajoDetalleModel(ICurrentUserContext currentUser, IGenericImportJobService jobs,
        ILogger<ImportacionTrabajoDetalleModel> logger) : base(currentUser)
    {
        _jobs = jobs;
        _logger = logger;
    }

    protected override string MenuCode => "ImportacionGenerica.trabajos";

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public GenericImportJobDto? Job { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Job = await _jobs.GetAsync(Id, ct);
        if (Job is null)
        {
            ErrorMessage ??= "La importación ya no existe (se descartó o venció).";
            return RedirectToPage("/Trabajos/ImportacionTrabajos");
        }

        return Page();
    }

    public async Task<JsonResult> OnGetProgressAsync(CancellationToken ct) => new(await _jobs.GetProgressAsync(Id, ct));

    /// <summary>Envía la carga guardada tal cual (el worker la vuelve a validar contra SAP antes de crear).</summary>
    public async Task<IActionResult> OnPostSendAsync(CancellationToken ct)
    {
        try
        {
            var job = await _jobs.GetAsync(Id, ct)
                ?? throw new InvalidOperationException("La carga ya no existe (otro usuario la descartó o venció).");
            await _jobs.EnqueueAsync(Id, job.Parameters, ct);
            SuccessMessage = "Carga enviada a SAP: se procesa en segundo plano. Las OC con errores de validación no se envían.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no previsto al enviar la carga guardada {JobId}.", Id);
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDiscardAsync(CancellationToken ct)
    {
        try
        {
            await _jobs.DiscardAsync(Id, ct);
            SuccessMessage = "Carga guardada descartada.";
            return RedirectToPage("/Trabajos/ImportacionTrabajos", new { view = "guardadas" });
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no previsto al descartar la carga guardada {JobId}.", Id);
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { id = Id });
    }
}
