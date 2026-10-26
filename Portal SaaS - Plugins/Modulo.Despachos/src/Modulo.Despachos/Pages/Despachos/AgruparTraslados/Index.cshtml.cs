using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using Modulo.Despachos.Pages.Despachos.Manifiestos;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.AgruparTraslados;

/// <summary>Listado de agrupaciones por rango de fechas de despacho, sucursal y estado. Menú "agrupar-traslados" (permisos: Despachos.agrupar-traslados).</summary>
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

    /// <summary>Un día de despacho (yyyy-MM-dd): lo usan los enlaces de Generar y de la campana; equivale a Desde = Hasta.</summary>
    [BindProperty(SupportsGet = true)] public string? DispatchDate { get; set; }
    /// <summary>Rango de fechas de despacho (yyyy-MM-dd); sin ninguna fecha = hoy.</summary>
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
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
            return RedirectToPage(new { From, To, WarehouseCode, Status });
        }
    }

    private DispatchGroupFilter Filter()
    {
        if (string.IsNullOrWhiteSpace(From) && string.IsNullOrWhiteSpace(To))
        {
            From = To = string.IsNullOrWhiteSpace(DispatchDate)
                ? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DispatchDate.Trim();
        }
        DispatchDate = null;
        return new DispatchGroupFilter(null,
            string.IsNullOrWhiteSpace(WarehouseCode) ? null : WarehouseCode.Trim(),
            string.IsNullOrWhiteSpace(Status) ? null : Status,
            Date(From), Date(To));
    }

    private static DateOnly? Date(string? s) =>
        DateOnly.TryParseExact(s?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
