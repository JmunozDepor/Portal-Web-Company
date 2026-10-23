using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.ConsultasSap;

/// <summary>
/// Mantenedor de las consultas SAP (factura / guía de traslado / líneas de factura) por compañía. Menú
/// "consultas-sap" (permisos: Despachos.consultas-sap). Guardar exige que la consulta pase
/// la prueba con un folio real; "Probar" ejecuta sin guardar y muestra el documento mapeado
/// o los errores.
/// </summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly ISapDocumentLookupService _lookup;

    public IndexModel(ISapDocumentLookupService lookup, ICurrentUserContext currentUser) : base(currentUser)
    {
        _lookup = lookup;
    }

    protected override string MenuCode => "consultas-sap";

    /// <summary>Una fila por tipo de documento; Query es null si la compañía no la tiene configurada.</summary>
    public IReadOnlyList<(string Kind, SapDocumentQuery? Query)> Rows { get; private set; } =
        Array.Empty<(string, SapDocumentQuery?)>();

    /// <summary>Resultado de la última prueba (solo tras el handler Probar).</summary>
    public SapQueryProbeResult? Probe { get; private set; }

    [BindProperty]
    public ConsultaInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(LoadRowsAsync);

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
    {
        var folio = RequireFolio();
        await _lookup.SaveQueryAsync(Input.DocumentKind ?? "", Input.SqlText ?? "", Input.IsActive, folio);
    }, "Consulta SAP validada y guardada.");

    public async Task<IActionResult> OnPostProbeAsync()
    {
        if (!await HasActionAsync(PortalActions.Edit) && !await HasActionAsync(PortalActions.Create)) return Forbid();
        await LoadPermissionsAsync();
        await LoadRowsAsync();
        try
        {
            Probe = await _lookup.ProbeAsync(Input.DocumentKind ?? "", Input.SqlText ?? "", RequireFolio());
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return Page();
    }

    private async Task LoadRowsAsync()
    {
        var queries = await _lookup.ListQueriesAsync();
        Rows = SapQueryKinds.All
            .Select(k => (k, queries.FirstOrDefault(q => q.DocumentKind == k)))
            .ToList();
    }

    private long RequireFolio()
    {
        if (Input.SampleFolio is not > 0)
            throw new InvalidOperationException(Input.DocumentKind == SapQueryKinds.InvoiceLines
                ? "Indique un DocEntry de prueba (número interno de una factura que exista en SAP)."
                : "Indique un folio de prueba (número de un documento que exista en SAP).");
        return Input.SampleFolio.Value;
    }

    public sealed class ConsultaInput
    {
        public long Id { get; set; }
        public string? DocumentKind { get; set; }
        public string? SqlText { get; set; }
        public bool IsActive { get; set; }
        public long? SampleFolio { get; set; }
    }
}
