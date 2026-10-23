using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.AgruparTraslados;

/// <summary>
/// Generar agrupaciones: transferencias SAP libres hacia las sucursales, agrupadas por sucursal y marcadas por defecto;
/// "Generar" crea una agrupación por sucursal (CREATE).
/// </summary>
public sealed class GenerarModel : DespachosPageModelBase
{
    private readonly IDispatchGroupService _groups;
    private readonly IMasterDataService _data;

    public GenerarModel(IDispatchGroupService groups, IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _groups = groups;
        _data = data;
    }

    protected override string MenuCode => "agrupar-traslados";

    public IReadOnlyList<BranchCandidates> Candidates { get; private set; } = Array.Empty<BranchCandidates>();
    public IReadOnlyList<BranchWarehouse> Branches { get; private set; } = Array.Empty<BranchWarehouse>();

    /// <summary>Transferencias desde (yyyy-MM-dd), por defecto hoy.</summary>
    [BindProperty(SupportsGet = true)] public string? FromDate { get; set; }
    /// <summary>Fecha de despacho (yyyy-MM-dd), por defecto hoy.</summary>
    [BindProperty(SupportsGet = true)] public string? DispatchDate { get; set; }
    [BindProperty(SupportsGet = true)] public string? WarehouseCode { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await HasActionAsync(PortalActions.Create)) return Forbid();
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(FromDate)) FromDate = today;
        if (string.IsNullOrWhiteSpace(DispatchDate)) DispatchDate = today;
        try
        {
            Branches = await _data.ListBranchWarehousesAsync();
            var from = ParseDate(FromDate, "Indique la fecha desde la que se buscan las transferencias.");
            Candidates = await _groups.ListCandidatesAsync(from, WarehouseCode);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostGenerarAsync(string? dispatchDate, string? fromDate, int[]? docEntries)
    {
        if (!await HasActionAsync(PortalActions.Create)) return Forbid();
        try
        {
            var dispatch = ParseDate(dispatchDate, "Indique la fecha de despacho.");
            var from = ParseDate(fromDate, "Indique la fecha desde la que se buscan las transferencias.");
            var created = await _groups.GenerateAsync(dispatch, from, docEntries ?? Array.Empty<int>());
            SuccessMessage = created.Count == 1 ? "Se generó 1 agrupación." : $"Se generaron {created.Count} agrupaciones.";
            return RedirectToPage("Index", new { DispatchDate = dispatch.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { FromDate = fromDate, DispatchDate = dispatchDate });
        }
    }

    private static DateOnly ParseDate(string? text, string error) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : throw new InvalidOperationException(error);
}
