using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica.Pages.Trabajos;

/// <summary>
/// "Mis importaciones" (2026-10-15) -- los trabajos de importación de la compañía activa
/// (en cola, en proceso, terminados o fallidos), con su resultado. La creación en SAP
/// corre en segundo plano (GenericImportJobWorker); esta pantalla solo lo consulta.
/// Nombre de archivo propio (no Index) -- evita la colisión de rutas Razor entre plugins.
///
/// Pestaña "Guardadas" (borradores etapa 2, 2026-10-21): cargas guardadas sin enviar.
/// Todos ven las de todos (decisión del dueño del proyecto); el filtro por defecto son las
/// propias (?creador=todos para ver todas).
/// </summary>
public sealed class ImportacionTrabajosModel : PageModelBaseAdmin
{
    private readonly IGenericImportJobService _jobs;

    public ImportacionTrabajosModel(ICurrentUserContext currentUser, IGenericImportJobService jobs) : base(currentUser)
    {
        _jobs = jobs;
    }

    /// <summary>"enviadas" (default) | "guardadas".</summary>
    [BindProperty(SupportsGet = true, Name = "view")]
    public string? View { get; set; }

    /// <summary>Solo en Guardadas: "mios" (default) | "todos".</summary>
    [BindProperty(SupportsGet = true, Name = "creador")]
    public string? Creator { get; set; }

    public bool ShowingSaved => string.Equals(View, "guardadas", StringComparison.OrdinalIgnoreCase);

    public bool ShowingAllCreators => string.Equals(Creator, "todos", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<GenericImportJobDto> Jobs { get; private set; } = [];

    public int SentCount { get; private set; }

    /// <summary>Guardadas según el filtro de creador vigente (para el contador de la pestaña).</summary>
    public int SavedCount { get; private set; }

    public bool AnyRunning => !ShowingSaved && Jobs.Any(j => !j.IsFinished);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var all = await _jobs.ListAsync(ct: ct);
        var saved = all.Where(j => j.IsSaved && (ShowingAllCreators || j.UserId == CurrentUser.UserId))
            .OrderByDescending(j => j.SavedAt ?? j.CreatedAt)
            .ToList();
        var sent = all.Where(j => !j.IsSaved).ToList();

        SavedCount = saved.Count;
        SentCount = sent.Count;
        Jobs = ShowingSaved ? saved : sent;
    }
}
