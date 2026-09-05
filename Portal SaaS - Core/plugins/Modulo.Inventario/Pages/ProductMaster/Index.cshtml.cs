using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Inventario.Pages.ProductMaster;

/// <summary>
/// Visor de solo lectura del maestro de artículos de SAP -- datos generales, código de
/// barra y grupo de artículo (OITM/OITB), precio en una lista de precios seleccionable
/// (OPLN/ITM1) y stock por almacén (OITW/OWHS). No es un motor de documento genérico
/// (no hereda de las bases Index/DetailGeneric*DocumentModelBase) -- es una consulta
/// puntual sobre un único artículo por código exacto.
/// </summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    private const string MenuCode = "Inventario.maestroproducto";

    private readonly IItemCatalogService _items;
    private readonly IItemMasterDetailService _itemMaster;
    private readonly IItemStockService _stock;
    private readonly IPriceListService _priceLists;
    private readonly ICurrentUserContext _currentUser;

    public IndexModel(
        IItemCatalogService items,
        IItemMasterDetailService itemMaster,
        IItemStockService stock,
        IPriceListService priceLists,
        ICurrentUserContext currentUser)
    {
        _items = items;
        _itemMaster = itemMaster;
        _stock = stock;
        _priceLists = priceLists;
        _currentUser = currentUser;
    }

    [BindProperty(SupportsGet = true)]
    public string? ItemCode { get; set; }

    public ItemMasterDetailDto? Detail { get; private set; }
    public bool ItemNotFound { get; private set; }
    public IReadOnlyList<PriceListOptionDto> PriceLists { get; private set; } = [];
    public int SelectedPriceList { get; private set; }
    public decimal? SelectedPrice { get; private set; }
    public IReadOnlyList<WarehouseStockDto> Stocks { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await _currentUser.HasActionAsync(MenuCode, PortalActions.View, ct))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(ItemCode))
        {
            return Page();
        }

        Detail = await _itemMaster.GetDetailAsync(ItemCode, ct);
        if (Detail is null)
        {
            ItemNotFound = true;
            return Page();
        }

        PriceLists = await _priceLists.ListAllAsync(ct);
        // Preselección: la lista de menor ListNum (pedido explícito, sin lógica de
        // negocio asociada a cliente/configuración).
        SelectedPriceList = PriceLists.Count > 0 ? PriceLists.Min(l => l.ListNum) : 0;
        SelectedPrice = PriceLists.Count > 0
            ? await _priceLists.GetPriceAsync(ItemCode, SelectedPriceList, ct)
            : null;
        Stocks = await _stock.GetStockByItemAsync(ItemCode, ct);

        return Page();
    }

    /// <summary>Búsqueda en vivo del artículo -- mismo mecanismo que Modulo.Ventas (nunca se precarga OITM completo).</summary>
    public async Task<JsonResult> OnGetSearchItemsAsync(string text, CancellationToken ct)
    {
        var items = await _items.SearchAsync(text ?? string.Empty, ct: ct);
        return new JsonResult(items.Select(i => new { i.ItemCode, i.ItemName }));
    }

    /// <summary>Fetch AJAX liviano al cambiar el combo de lista de precios -- evita recargar toda la ficha.</summary>
    public async Task<JsonResult> OnGetPriceAsync(string itemCode, int priceList, CancellationToken ct)
    {
        var price = await _priceLists.GetPriceAsync(itemCode, priceList, ct);
        return new JsonResult(new { price });
    }
}
