using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Inventario.Pages;

/// <summary>
/// Crear/ver un documento de inventario genérico -- solo creación, sin
/// IInventoryDocumentService.UpdateAsync todavía (digitación directa: se crea una vez,
/// después es de solo lectura vía el portal). Mismo patrón que
/// DetailGenericSalesDocumentModelBase (Modulo.Ventas) -- copia deliberada, no una base
/// compartida entre plugins.
///
/// REGLA DURA (ver CLAUDE.md): todo nace del documento padre. OnGetAsync/OnPostAsync/
/// OnGetSearchItemsAsync NO son virtual a propósito -- ningún subtipo puede modificar
/// cómo se crea/valida/lee un documento, solo puede identificarse. Cualquier tipo de
/// documento de inventario nuevo se agrega acá (InventoryDocumentTypeCatalog + este
/// subtipo), nunca reimplementando esta clase.
/// </summary>
[Authorize]
public abstract class DetailGenericInventoryDocumentModelBase : PageModel
{
    private const string NewId = "nuevo";

    private readonly IInventoryDocumentService _documents;
    private readonly ICurrentUserContext _currentUser;
    private readonly IWarehouseCatalogService _warehouses;
    private readonly IItemCatalogService _items;

    protected DetailGenericInventoryDocumentModelBase(
        IInventoryDocumentService documents,
        ICurrentUserContext currentUser,
        IWarehouseCatalogService warehouses,
        IItemCatalogService items)
    {
        _documents = documents;
        _currentUser = currentUser;
        _warehouses = warehouses;
        _items = items;
    }

    protected abstract InventoryDocumentType Type { get; }
    protected abstract string MenuCode { get; }
    public abstract string DocumentName { get; }
    public abstract string RouteBase { get; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsNew { get; private set; }
    public int? DocEntry { get; private set; }
    public int? DocNum { get; private set; }
    public string? Status { get; private set; }

    public List<SelectListItem> Warehouses { get; private set; } = [];

    public DocumentFormViewModel Document { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken ct)
    {
        IsNew = id == NewId;

        var requiredAction = IsNew ? PortalActions.Create : PortalActions.View;
        if (!await _currentUser.HasActionAsync(MenuCode, requiredAction, ct))
        {
            return Forbid();
        }

        if (IsNew)
        {
            if (!await _documents.CanCreateAsync(Type, ct))
            {
                return Forbid();
            }

            Input = new InputModel
            {
                DocDate = DateOnly.FromDateTime(DateTime.Today),
            };
        }
        else
        {
            if (!int.TryParse(id, out var docEntry))
            {
                return NotFound();
            }

            var document = await _documents.GetAsync(Type, docEntry, ct);
            if (document is null)
            {
                return NotFound();
            }

            DocEntry = document.DocEntry;
            DocNum = document.DocNum;
            Status = document.Status;

            Input = new InputModel
            {
                Comments = document.Comments,
                DocDate = document.DocDate,
                Lines = document.Lines.Select(line => new LineInput
                {
                    ItemCode = line.ItemCode,
                    Description = line.Description,
                    Quantity = line.Quantity,
                    FromWarehouseCode = line.FromWarehouseCode,
                    ToWarehouseCode = line.ToWarehouseCode,
                }).ToList(),
            };
        }

        await LoadCatalogsAsync(ct);
        BuildDocumentViewModel();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        IsNew = true;

        if (!await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct) || !await _documents.CanCreateAsync(Type, ct))
        {
            return Forbid();
        }

        Input.Lines = Input.Lines.Where(line => !string.IsNullOrWhiteSpace(line.ItemCode)).ToList();

        if (Input.Lines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, $"Agregá al menos una línea antes de crear {DocumentName.ToLowerInvariant()}.");
        }

        for (var i = 0; i < Input.Lines.Count; i++)
        {
            var line = Input.Lines[i];
            if (line.Quantity is null or <= 0)
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.Quantity)}", "La cantidad debe ser mayor a 0.");
            }

            if (string.IsNullOrWhiteSpace(line.FromWarehouseCode))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.FromWarehouseCode)}", "Elegí un almacén origen para esta línea.");
            }

            if (string.IsNullOrWhiteSpace(line.ToWarehouseCode))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.ToWarehouseCode)}", "Elegí un almacén destino para esta línea.");
            }
            else if (line.ToWarehouseCode == line.FromWarehouseCode)
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.ToWarehouseCode)}", "El almacén destino debe ser distinto del origen.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadCatalogsAsync(ct);
            BuildDocumentViewModel();
            return Page();
        }

        var document = new InventoryDocumentDto(
            DocDate: Input.DocDate,
            Comments: Input.Comments,
            Lines: Input.Lines.Select(line => new InventoryDocumentLineDto(
                ItemCode: line.ItemCode!,
                Description: line.Description,
                Quantity: line.Quantity ?? 0,
                FromWarehouseCode: line.FromWarehouseCode!,
                ToWarehouseCode: line.ToWarehouseCode!)).ToList());

        try
        {
            var docEntry = await _documents.CreateAsync(Type, _currentUser.Username, document, ct);
            return RedirectToPage(new { id = docEntry.ToString() });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el documento: {ex.Message}");
            await LoadCatalogsAsync(ct);
            BuildDocumentViewModel();
            return Page();
        }
    }

    /// <summary>Búsqueda en vivo del artículo -- nunca se precarga el catálogo completo (ver IItemCatalogService).</summary>
    public async Task<JsonResult> OnGetSearchItemsAsync(string text, CancellationToken ct)
    {
        var items = await _items.SearchAsync(text ?? string.Empty, ct: ct);
        return new JsonResult(items.Select(i => new { i.ItemCode, i.ItemName }));
    }

    private async Task LoadCatalogsAsync(CancellationToken ct)
    {
        var warehouses = await _warehouses.ListAsync(ct);
        Warehouses = warehouses.Select(w => new SelectListItem($"{w.WarehouseCode} — {w.WarehouseName}", w.WarehouseCode)).ToList();
    }

    private void BuildDocumentViewModel()
    {
        Document = new DocumentFormViewModel
        {
            Title = IsNew ? $"Nuevo/a {DocumentName}" : $"{DocumentName} N° {DocNum}",
            ReadOnly = !IsNew,
            BackUrl = RouteBase,
            StatusText = Status,
            StatusClass = Status == "Abierto" ? "bg-success" : "bg-secondary",
            Model = this,
            GeneralView = "~/Pages/Shared/_TabGeneral.cshtml",
            ContentView = "~/Pages/Shared/_TabContent.cshtml",
        };
    }

    public sealed class InputModel
    {
        [Display(Name = "Comentarios")]
        public string? Comments { get; set; }

        [Display(Name = "Fecha de documento")]
        public DateOnly DocDate { get; set; }

        public List<LineInput> Lines { get; set; } = [new()];
    }

    public sealed class LineInput
    {
        public string? ItemCode { get; set; }
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public string? FromWarehouseCode { get; set; }
        public string? ToWarehouseCode { get; set; }
    }
}
