using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica.Pages.Trabajos;

/// <summary>
/// "Mis importaciones" (2026-10-15) -- los trabajos de importación de la compañía activa
/// (en cola, en proceso, terminados o fallidos), con su resultado. La creación en SAP
/// corre en segundo plano (GenericImportJobWorker); esta pantalla solo lo consulta.
/// Nombre de archivo propio (no Index) -- evita la colisión de rutas Razor entre plugins.
/// </summary>
public sealed class ImportacionTrabajosModel : PageModelBaseAdmin
{
    private readonly IGenericImportJobService _jobs;

    public ImportacionTrabajosModel(ICurrentUserContext currentUser, IGenericImportJobService jobs) : base(currentUser)
    {
        _jobs = jobs;
    }

    public IReadOnlyList<GenericImportJobDto> Jobs { get; private set; } = [];

    public bool AnyRunning => Jobs.Any(j => !j.IsFinished);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Jobs = await _jobs.ListAsync(ct: ct);
    }
}
