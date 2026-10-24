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
        var (folio, text) = RequireSample();
        await _lookup.SaveQueryAsync(Input.DocumentKind ?? "", Input.SqlText ?? "", Input.IsActive, folio, text);
    }, "Consulta SAP validada y guardada.");

    public async Task<IActionResult> OnPostProbeAsync()
    {
        if (!await HasActionAsync(PortalActions.Edit) && !await HasActionAsync(PortalActions.Create)) return Forbid();
        await LoadPermissionsAsync();
        await LoadRowsAsync();
        try
        {
            var (folio, text) = RequireSample();
            Probe = await _lookup.ProbeAsync(Input.DocumentKind ?? "", Input.SqlText ?? "", folio, text);
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

    /// <summary>
    /// Muestra de prueba según el tipo: LPN o código de bodega como texto; folio o DocEntry como número; las
    /// bodegas no usan muestra.
    /// </summary>
    private (long Folio, string? Text) RequireSample()
    {
        var sample = Input.Sample?.Trim() ?? "";
        switch (Input.DocumentKind)
        {
            case SapQueryKinds.Warehouses:
            case SapQueryKinds.TransferPendingAll:
                return (0, null);
            case SapQueryKinds.TransferByLpn:
                if (sample.Length == 0) throw new InvalidOperationException("Indique un LPN de prueba (de una transferencia que exista en SAP).");
                return (0, sample);
            case SapQueryKinds.TransferPending:
                if (sample.Length == 0) throw new InvalidOperationException("Indique el código de una bodega de prueba (con transferencias de los últimos 30 días).");
                return (0, sample);
            case SapQueryKinds.BusinessPartnerByTaxId:
                if (sample.Length == 0) throw new InvalidOperationException("Indique un RUT de prueba (de un cliente que exista en SAP).");
                return (0, sample);
        }
        if (!long.TryParse(sample, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0)
            throw new InvalidOperationException(Input.DocumentKind is SapQueryKinds.InvoiceLines or SapQueryKinds.TransferLines
                ? "Indique un DocEntry de prueba (número interno de un documento que exista en SAP)."
                : "Indique un folio de prueba (número de un documento que exista en SAP).");
        return (n, null);
    }

    /// <summary>Etiqueta del campo de muestra según el tipo de consulta.</summary>
    public static string SampleLabel(string? kind) => kind switch
    {
        SapQueryKinds.InvoiceLines or SapQueryKinds.TransferLines => "DocEntry de prueba",
        SapQueryKinds.TransferByLpn => "LPN de prueba",
        SapQueryKinds.TransferPending => "Código de bodega de prueba",
        SapQueryKinds.BusinessPartnerByTaxId => "RUT de prueba",
        SapQueryKinds.Warehouses => "Sin muestra (la consulta no usa parámetros)",
        SapQueryKinds.TransferPendingAll => "Sin muestra (prueba con los últimos 30 días)",
        _ => "Folio de prueba",
    };

    public sealed class ConsultaInput
    {
        public long Id { get; set; }
        public string? DocumentKind { get; set; }
        public string? SqlText { get; set; }
        public bool IsActive { get; set; }
        public string? Sample { get; set; }
    }
}
