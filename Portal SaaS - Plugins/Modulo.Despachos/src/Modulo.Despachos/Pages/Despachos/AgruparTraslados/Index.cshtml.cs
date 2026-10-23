using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using Modulo.Despachos.Pages.Despachos.Manifiestos;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.AgruparTraslados;

/// <summary>Listado de agrupaciones por fecha de despacho, sucursal y estado. Menú "agrupar-traslados" (permisos: Despachos.agrupar-traslados).</summary>
public sealed class IndexModel : DespachosPageModelBase
{
    private readonly IDispatchGroupService _groups;
    private readonly IDispatchGroupPdfService _pdf;
    private readonly IMasterDataService _data;

    public IndexModel(IDispatchGroupService groups, IDispatchGroupPdfService pdf, IMasterDataService data, ICurrentUserContext currentUser)
        : base(currentUser)
    {
        _groups = groups;
        _pdf = pdf;
        _data = data;
    }

    protected override string MenuCode => "agrupar-traslados";

    public bool CanCreate { get; private set; }
    public IReadOnlyList<DispatchGroupListItem> Items { get; private set; } = Array.Empty<DispatchGroupListItem>();
    public IReadOnlyList<BranchWarehouse> Branches { get; private set; } = Array.Empty<BranchWarehouse>();

    /// <summary>Fecha de despacho (yyyy-MM-dd); vacía = hoy. "todas" = sin filtro de fecha.</summary>
    [BindProperty(SupportsGet = true)] public string? DispatchDate { get; set; }
    [BindProperty(SupportsGet = true)] public string? WarehouseCode { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        CanCreate = await HasActionAsync(PortalActions.Create);
        try
        {
            Branches = await _data.ListBranchWarehousesAsync(includeInactive: true);
            Items = await _groups.ListAsync(Filter());
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return Page();
    }

    /// <summary>Hojas de todas las agrupaciones no anuladas del filtro, en un solo PDF.</summary>
    public async Task<IActionResult> OnGetPdfAsync()
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            var ids = (await _groups.ListAsync(Filter()))
                .Where(x => x.Status != DispatchGroupStatuses.Annulled)
                .OrderBy(x => x.WarehouseCode).ThenBy(x => x.Sequence)
                .Select(x => x.Id).ToList();
            return new InlinePdfResult(await _pdf.RenderAsync(ids), "agrupar-traslados.pdf");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { DispatchDate, WarehouseCode, Status });
        }
    }

    private DispatchGroupFilter Filter()
    {
        if (string.IsNullOrWhiteSpace(DispatchDate)) DispatchDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateOnly? date = DateOnly.TryParseExact(DispatchDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
        return new DispatchGroupFilter(date,
            string.IsNullOrWhiteSpace(WarehouseCode) ? null : WarehouseCode.Trim(),
            string.IsNullOrWhiteSpace(Status) ? null : Status);
    }
}
