using Microsoft.AspNetCore.Mvc;
using Modulo.FacturasProveedor.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.FacturasProveedor.Pages.FacturasProveedor.Configuracion.ConsultasSap;

/// <summary>Consultas SAP configurables con "Probar" (patrón Despachos › Consultas SAP).</summary>
public sealed class IndexModel : FacturasProveedorPageModelBase
{
    private readonly ISapLookupService _sap;

    public IndexModel(ISapLookupService sap, ICurrentUserContext currentUser) : base(currentUser) => _sap = sap;

    protected override string MenuCode => "consultas-sap";

    public IReadOnlyList<SapQueryDto> Queries { get; private set; } = Array.Empty<SapQueryDto>();
    public bool CanEdit { get; private set; }

    /// <summary>Resultado de la última prueba (se muestra en la misma página, sin redirección).</summary>
    public (string Kind, QueryValidationResult Result)? TestResult { get; private set; }

    /// <summary>SQL editado que se probó (para no perderlo si la prueba falla).</summary>
    public Dictionary<string, string> Drafts { get; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(string kind, string sql, bool isActive, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            await _sap.SaveQueryAsync(kind, sql, isActive, ct);
            SuccessMessage = $"Consulta «{SapQueryKinds.Label(kind)}» guardada.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostProbarAsync(string kind, string sql, string sample, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        TestResult = (kind, await _sap.TestQueryAsync(kind, sql, sample ?? "", ct));
        Drafts[kind] = sql;
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        CanEdit = await HasActionAsync(PortalActions.Edit);
        Queries = await _sap.ListQueriesAsync(ct);
    }
}
