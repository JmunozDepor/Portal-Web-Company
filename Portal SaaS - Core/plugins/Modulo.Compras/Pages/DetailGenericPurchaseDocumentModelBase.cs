using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages;

/// <summary>
/// Crear/ver un documento de compra genérico -- solo creación, sin
/// IPurchaseDocumentService.UpdateAsync todavía (digitación directa: se crea una vez,
/// después es de solo lectura vía el portal). Mismo patrón que
/// DetailGenericSalesDocumentModelBase (Modulo.Ventas) -- copia deliberada, no una base
/// compartida entre plugins.
///
/// REGLA DURA (ver CLAUDE.md): todo nace del documento padre. OnGetAsync/OnPostAsync/
/// OnGetSearchItemsAsync NO son virtual a propósito -- ningún subtipo puede modificar
/// cómo se crea/valida/lee un documento, solo puede identificarse. Cualquier tipo de
/// documento de compra nuevo se agrega acá (PurchaseDocumentTypeCatalog + este
/// subtipo), nunca reimplementando esta clase.
/// </summary>
[Authorize]
public abstract class DetailGenericPurchaseDocumentModelBase : PageModel
{
    private const string NewId = "nuevo";

    private readonly IPurchaseDocumentService _documents;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISupplierCatalogService _suppliers;
    private readonly IWarehouseCatalogService _warehouses;
    private readonly IItemCatalogService _items;
    private readonly IGeneralLedgerAccountCatalogService _accounts;
    private readonly ICostCenterCatalogService _costCenters;
    private readonly ISeriesCatalogService _series;
    private readonly ISalesEmployeeCatalogService _buyers;
    private readonly IPriceListService _priceLists;
    private readonly IBusinessPartnerDefaultsService _partnerDefaults;
    private readonly ICurrencyCatalogService _currencies;

    protected DetailGenericPurchaseDocumentModelBase(
        IPurchaseDocumentService documents,
        ICurrentUserContext currentUser,
        ISupplierCatalogService suppliers,
        IWarehouseCatalogService warehouses,
        IItemCatalogService items,
        IGeneralLedgerAccountCatalogService accounts,
        ICostCenterCatalogService costCenters,
        ISeriesCatalogService series,
        ISalesEmployeeCatalogService buyers,
        IPriceListService priceLists,
        IBusinessPartnerDefaultsService partnerDefaults,
        ICurrencyCatalogService currencies)
    {
        _documents = documents;
        _currentUser = currentUser;
        _suppliers = suppliers;
        _warehouses = warehouses;
        _items = items;
        _accounts = accounts;
        _costCenters = costCenters;
        _series = series;
        _buyers = buyers;
        _priceLists = priceLists;
        _partnerDefaults = partnerDefaults;
        _currencies = currencies;
    }

    protected abstract PurchaseDocumentType Type { get; }
    protected abstract string MenuCode { get; }
    public abstract string DocumentName { get; }
    public abstract string RouteBase { get; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>
    /// URL del listado que llevó acá (con su página/filtros actuales) -- IndexGeneric*ModelBase
    /// la arma al construir cada DetailUrl, para que "Volver" no resetee la paginación
    /// (bug real reportado: "Volver" desde la página 3 del listado volvía siempre a la
    /// página 1, porque BackUrl era RouteBase fijo, sin querystring). Url.IsLocalUrl,
    /// mismo criterio anti-open-redirect que Account/Login.cshtml.cs.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    public bool IsNew { get; private set; }
    public int? DocEntry { get; private set; }
    public int? DocNum { get; private set; }
    public decimal? DocTotal { get; private set; }
    public string? Status { get; private set; }

    /// <summary>Ver DocumentFormViewModel.CanClose para el criterio completo.</summary>
    public bool CanClose { get; private set; }

    /// <summary>Ver DocumentFormViewModel.CanCancel para el criterio completo.</summary>
    public bool CanCancel { get; private set; }

    /// <summary>Ver el doc-comment de CustomerName en DetailGenericSalesDocumentModelBase (Modulo.Ventas) -- mismo criterio, lado proveedor.</summary>
    public string? SupplierName { get; private set; }

    public List<SelectListItem> Series { get; private set; } = [];

    /// <summary>Comprador -- mismo catálogo que el Vendedor de Venta (OSLP), SAP SalesPersonCode.</summary>
    public List<SelectListItem> Buyers { get; private set; } = [];
    public List<SelectListItem> PriceLists { get; private set; } = [];
    public List<SelectListItem> Currencies { get; private set; } = [];

    /// <summary>Moneda local de la compañía (OADM.MainCurncy) -- default de Moneda cuando el proveedor no tiene una fija.</summary>
    public string? LocalCurrency { get; private set; }

    /// <summary>
    /// "Resumen total" del tab Contenido -- mismo cálculo que
    /// DetailGenericSalesDocumentModelBase (Modulo.Ventas), portado ahí primero de
    /// DetalleGenericoVentaModelBase (referencia-original/PortalSAP_v2), replicado
    /// acá por la regla de paridad: Compras también tiene precio/descuento por línea.
    /// </summary>
    public decimal TotalBeforeDiscount => Input.Lines.Sum(l => (l.Quantity ?? 0) * (l.UnitPrice ?? 0));
    public decimal Discount => Input.Lines.Sum(l => (l.Quantity ?? 0) * (l.UnitPrice ?? 0) * (l.DiscountPercent ?? 0) / 100m);
    public decimal AdditionalExpenses => 0m;
    public decimal DocumentTotal => DocTotal ?? (TotalBeforeDiscount - Discount);
    public decimal Tax => DocumentTotal - (TotalBeforeDiscount - Discount) - AdditionalExpenses;

    public DocumentFormViewModel Document { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string id, [FromQuery(Name = "draft")] Guid? draft, CancellationToken ct)
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
                DocDueDate = DateOnly.FromDateTime(DateTime.Today),
                TaxDate = DateOnly.FromDateTime(DateTime.Today),
                LineType = DocumentLineType.Item,
                Series = null,
                DocCurrency = await _currencies.GetLocalCurrencyAsync(ct),
            };

            if (draft is { } draftId)
            {
                var loaded = await Drafts.GetAsync(draftId, ct);
                if (loaded is null || loaded.Engine != DraftEngine || loaded.DocumentType != Type.ToString())
                {
                    return NotFound();
                }

                // Ya enviado: se abre el documento real de SAP, no un formulario editable.
                if (loaded.Status == DocumentDraftStatus.Sent && loaded.SapDocEntry is { } sentEntry)
                {
                    return RedirectToPage(new { id = sentEntry.ToString() });
                }

                Input = JsonSerializer.Deserialize<InputModel>(loaded.PayloadJson) ?? Input;
                DraftId = loaded.Id;
                DraftReason = loaded.Reason;
            }
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
            DocTotal = document.DocTotal;
            Status = document.Status;
            SupplierName = document.SupplierName;
            CanClose = Status != "Cerrado"
                && await _documents.CanCreateAsync(Type, ct)
                && await _currentUser.HasActionAsync(MenuCode, PortalActions.Delete, ct);
            CanCancel = CanClose && _documents.SupportsCancel(Type);

            Input = new InputModel
            {
                SupplierCardCode = document.SupplierCardCode,
                Comments = document.Comments,
                DocDate = document.DocDate,
                DocDueDate = document.DocDueDate,
                TaxDate = document.TaxDate,
                SupplierReferenceNumber = document.SupplierReferenceNumber,
                LineType = document.Lines.Count > 0 ? document.Lines[0].Type : DocumentLineType.Item,
                Series = document.Series,
                BuyerCode = document.BuyerCode,
                DocCurrency = document.DocCurrency,
                Lines = document.Lines.Select(line => new LineInput
                {
                    ItemCode = line.ItemCode,
                    Description = line.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountPercent = line.DiscountPercent,
                    WarehouseCode = line.WarehouseCode,
                    AccountCode = line.AccountCode,
                    CostCenterCode = line.CostCenterCode,
                    CostCenterCode2 = line.CostCenterCode2,
                    CostCenterCode3 = line.CostCenterCode3,
                    Currency = line.Currency,
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

        // Enviar un borrador: primero se guarda lo editado, así no se pierde aunque después
        // falle la validación o SAP lo rechace.
        if (DraftId is not null && !_validateOnly)
        {
            try
            {
                await SaveDraftAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return await PageWithErrorAsync(ex.Message, ct);
            }
        }

        var isService = Input.LineType == DocumentLineType.Service;

        Input.Lines = Input.Lines
            .Where(line => isService ? !string.IsNullOrWhiteSpace(line.Description) : !string.IsNullOrWhiteSpace(line.ItemCode))
            .ToList();

        if (Input.Lines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, $"Agregá al menos una línea antes de crear {DocumentName.ToLowerInvariant()}.");
        }

        for (var i = 0; i < Input.Lines.Count; i++)
        {
            var line = Input.Lines[i];

            if (isService)
            {
                if (string.IsNullOrWhiteSpace(line.AccountCode))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.AccountCode)}", "Elegí una cuenta mayor para esta línea.");
                }

                if (string.IsNullOrWhiteSpace(line.CostCenterCode))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.CostCenterCode)}", "Elegí un centro de costos para esta línea.");
                }
            }
            else if (string.IsNullOrWhiteSpace(line.WarehouseCode))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.WarehouseCode)}", "Elegí un almacén para esta línea.");
            }

            if (line.Quantity is null or <= 0)
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.Quantity)}", "La cantidad debe ser mayor a 0.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadCatalogsAsync(ct);
            BuildDocumentViewModel();
            return Page();
        }

        // Líneas de Artículo sin precio digitado: misma regla que el autocompletado del
        // formulario (precio especial del proveedor, si no la lista elegida) -- ver el
        // comentario equivalente en DetailGenericSalesDocumentModelBase (Modulo.Ventas).
        if (!isService)
        {
            await FillMissingPricesAsync(ct);
        }

        await AddLineRestrictionErrorsAsync(ct);
        if (!ModelState.IsValid)
        {
            await LoadCatalogsAsync(ct);
            BuildDocumentViewModel();
            return Page();
        }

        var document = new PurchaseDocumentDto(
            SupplierCardCode: Input.SupplierCardCode,
            SupplierName: null,
            Comments: Input.Comments,
            DocDate: Input.DocDate,
            DocDueDate: Input.DocDueDate,
            TaxDate: Input.TaxDate,
            SupplierReferenceNumber: Input.SupplierReferenceNumber,
            Lines: Input.Lines.Select(line => new PurchaseDocumentLineDto(
                Type: Input.LineType,
                ItemCode: isService ? null : line.ItemCode,
                Description: line.Description,
                Quantity: line.Quantity ?? 0,
                UnitPrice: line.UnitPrice,
                DiscountPercent: line.DiscountPercent ?? 0,
                WarehouseCode: isService ? null : line.WarehouseCode,
                AccountCode: isService ? line.AccountCode : null,
                CostCenterCode: isService ? line.CostCenterCode : null,
                CostCenterCode2: isService ? line.CostCenterCode2 : null,
                CostCenterCode3: isService ? line.CostCenterCode3 : null,
                Currency: line.UnitPrice is null ? null : line.Currency)).ToList(),
            Series: Input.Series,
            BuyerCode: Input.BuyerCode,
            DocCurrency: Input.DocCurrency);

        if (_validateOnly)
        {
            return await SimulateAsync(document, ct);
        }

        var sendingDraft = false;
        try
        {
            if (DraftId is { } draftToSend)
            {
                if (!await Drafts.TryBeginSendAsync(draftToSend, ct))
                {
                    return await PageWithErrorAsync("Este borrador ya fue enviado o lo está enviando otro usuario.", ct);
                }

                sendingDraft = true;

                // Marca de origen + verificación (ver IDocumentSapMarkerService): si un envío
                // anterior de este borrador llegó a SAP pero no alcanzó a registrarse (servidor
                // caído, timeout), el documento ya existe -- se concilia en vez de duplicarlo.
                var draftKey = draftToSend.ToString("N");
                var existing = await Markers.FindDocEntryAsync(DraftEngine, Type.ToString(), draftKey, ct);
                if (existing is { } existingEntry)
                {
                    await Drafts.MarkSentAsync(draftToSend, existingEntry, $"{Request.PathBase}{RouteBase}/{existingEntry}", ct);
                    TempData["DocumentActionMessage"] = $"Este borrador ya estaba creado en SAP (DocEntry {existingEntry}) -- no se volvió a crear.";
                    TempData["DocumentActionMessageType"] = "alert-info";
                    return RedirectToPage(new { id = existingEntry.ToString() });
                }

                var marker = await Markers.GetMarkerAsync(draftKey, ct);
                document = document with
                {
                    Comments = marker.ApplyToComments(document.Comments),
                    AdditionalFields = marker.ApplyToAdditionalFields(document.AdditionalFields),
                };
            }

            var docEntry = await _documents.CreateAsync(Type, _currentUser.Username, document, ct);
            if (sendingDraft)
            {
                try
                {
                    await Drafts.MarkSentAsync(DraftId!.Value, docEntry, $"{Request.PathBase}{RouteBase}/{docEntry}", ct);
                }
                catch (Exception)
                {
                    // El documento YA existe en SAP: nunca convertir esto en error ni dejar
                    // reintentar como si no se hubiera creado. El borrador queda "Enviando".
                }
            }

            return RedirectToPage(new { id = docEntry.ToString() });
        }
        catch (Exception ex)
        {
            if (sendingDraft)
            {
                await Drafts.MarkErrorAsync(DraftId!.Value, ex.Message, CancellationToken.None);
            }

            ModelState.AddModelError(string.Empty, $"No se pudo crear el documento: {ex.Message}");
            await LoadCatalogsAsync(ct);
            BuildDocumentViewModel();
            return Page();
        }
    }

    /// <summary>
    /// Cierra el documento en SAP (ver IPurchaseDocumentService.CloseAsync) -- validado
    /// en el servidor con el mismo gate que habilita el botón (CanCreateAsync +
    /// PortalActions.Delete), portado de OnPostCerrarAsync (DetalleGenericoVentaModelBase,
    /// referencia-original/PortalSAP_v2).
    /// </summary>
    public Task<IActionResult> OnPostCloseAsync(string id, CancellationToken ct) =>
        ExecuteStatusActionAsync(id, docEntry => _documents.CloseAsync(Type, docEntry, ct), "cerrado", requireSupportsCancel: false, ct);

    /// <summary>
    /// Cancela el documento en SAP (ver IPurchaseDocumentService.CancelAsync) -- mismo
    /// gate que OnPostCloseAsync, MÁS SupportsCancel(Type) (revalidado server-side, no
    /// solo ocultando el botón): Service Layer rechaza "/Cancel" sobre documentos de
    /// intención (Oferta/Pedido de Compra) con "The requested action is not supported
    /// for this object" -- mismo error real confirmado en Inventario, 2026-09-04.
    /// </summary>
    public Task<IActionResult> OnPostCancelAsync(string id, CancellationToken ct) =>
        ExecuteStatusActionAsync(id, docEntry => _documents.CancelAsync(Type, docEntry, ct), "cancelado", requireSupportsCancel: true, ct);

    private async Task<IActionResult> ExecuteStatusActionAsync(string id, Func<int, Task> action, string resultParticiple, bool requireSupportsCancel, CancellationToken ct)
    {
        if (!int.TryParse(id, out var docEntry)
            || !await _documents.CanCreateAsync(Type, ct)
            || !await _currentUser.HasActionAsync(MenuCode, PortalActions.Delete, ct)
            || (requireSupportsCancel && !_documents.SupportsCancel(Type)))
        {
            return Forbid();
        }

        try
        {
            await action(docEntry);
            TempData["DocumentActionMessage"] = $"{DocumentName} {resultParticiple} correctamente.";
            TempData["DocumentActionMessageType"] = "alert-success";
        }
        catch (Exception ex)
        {
            TempData["DocumentActionMessage"] = $"No se pudo {(resultParticiple == "cerrado" ? "cerrar" : "cancelar")} el documento: {ex.Message}";
            TempData["DocumentActionMessageType"] = "alert-danger";
        }

        return RedirectToPage(new { id });
    }

    /// <summary>Búsqueda en vivo del artículo -- nunca se precarga el catálogo completo (ver IItemCatalogService).</summary>
    public async Task<JsonResult> OnGetSearchItemsAsync(string text, CancellationToken ct)
    {
        var items = await _items.SearchAsync(text ?? string.Empty, ct: ct);
        return new JsonResult(items.Select(i => new { i.ItemCode, i.ItemName }));
    }

    /// <summary>Ver el comentario completo en OnGetValidateItemCodesAsync (DetailGenericSalesDocumentModelBase, Modulo.Ventas), mismo criterio.</summary>
    public async Task<JsonResult> OnGetValidateItemCodesAsync(string[] codes, CancellationToken ct)
    {
        var distinctCodes = (codes ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var items = await _items.GetByCodesAsync(distinctCodes, ct);
        return new JsonResult(items.Select(i => new { i.ItemCode, i.ItemName }));
    }

    /// <summary>Búsqueda en vivo del proveedor -- ver el comentario completo en OnGetSearchCustomersAsync (DetailGenericSalesDocumentModelBase, Modulo.Ventas), mismo criterio lado proveedor.</summary>
    public async Task<JsonResult> OnGetSearchSuppliersAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonResult(Array.Empty<object>());
        }

        var suppliers = await _suppliers.ListAsync(new SupplierFilter(text, Limit: 30), ct);
        return new JsonResult(suppliers.Select(s => new { s.CardCode, s.CardName }));
    }

    /// <summary>Ver el comentario completo en OnGetSearchWarehousesAsync (DetailGenericSalesDocumentModelBase, Modulo.Ventas) -- catálogo chico, sin guard de texto vacío.</summary>
    public async Task<JsonResult> OnGetSearchWarehousesAsync(string text, CancellationToken ct)
    {
        var warehouses = await _warehouses.ListAsync(text, 30, ct);
        return new JsonResult(warehouses.Select(w => new { w.WarehouseCode, w.WarehouseName }));
    }

    public async Task<JsonResult> OnGetSearchAccountsAsync(string text, CancellationToken ct)
    {
        var accounts = await _accounts.ListAsync(text, 30, ct);
        return new JsonResult(accounts.Select(a => new { a.AccountCode, a.AccountName }));
    }

    public async Task<JsonResult> OnGetSearchCostCentersAsync(string text, CancellationToken ct)
    {
        var costCenters = await _costCenters.ListAsync(text, 30, ct);
        return new JsonResult(costCenters.Select(c => new { c.Code, c.Name }));
    }

    public async Task<JsonResult> OnGetSearchCostCenters2Async(string text, CancellationToken ct)
    {
        var costCenters = await _costCenters.ListByDimensionAsync(2, text, 30, ct);
        return new JsonResult(costCenters.Select(c => new { c.Code, c.Name }));
    }

    public async Task<JsonResult> OnGetSearchCostCenters3Async(string text, CancellationToken ct)
    {
        var costCenters = await _costCenters.ListByDimensionAsync(5, text, 30, ct);
        return new JsonResult(costCenters.Select(c => new { c.Code, c.Name }));
    }

    /// <summary>
    /// Valores por defecto del proveedor (OCRD) al elegirlo: Comprador (SlpCode), Lista de
    /// precio (ListNum) y Moneda (Currency; "##" = todas, cae a la local). Quedan
    /// modificables -- mismo criterio que OnGetPartnerDefaultsAsync de Modulo.Ventas.
    /// </summary>
    public async Task<JsonResult> OnGetPartnerDefaultsAsync(string cardCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
        {
            return new JsonResult(null);
        }

        var defaults = await _partnerDefaults.GetAsync(cardCode.Trim(), ct);
        if (defaults is null)
        {
            return new JsonResult(null);
        }

        return new JsonResult(new
        {
            Currency = await ResolvePartnerCurrencyAsync(defaults.Currency, ct),
            // SlpCode -1 = "sin comprador" en SAP -- se trata como sin asignar.
            SalesEmployeeCode = defaults.SalesEmployeeCode is > 0 ? defaults.SalesEmployeeCode : null,
            defaults.PriceListCode,
        });
    }

    /// <summary>Precio de varios artículos para el proveedor: precio especial (SPP1/OSPP) o, si no tiene, el de la lista elegida, con su moneda.</summary>
    public async Task<JsonResult> OnGetLinePricesAsync(string? cardCode, int? priceList, DateOnly? date, string[] codes, CancellationToken ct)
    {
        var prices = await ResolvePricesAsync(cardCode, priceList, date ?? DateOnly.FromDateTime(DateTime.Today), codes ?? [], ct);
        return new JsonResult(prices.Select(p => new { ItemCode = p.Key, p.Value.Price, p.Value.Currency, p.Value.Source }));
    }

    private async Task<string?> ResolvePartnerCurrencyAsync(string? partnerCurrency, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(partnerCurrency) || partnerCurrency.Trim() == "##"
            ? await _currencies.GetLocalCurrencyAsync(ct)
            : partnerCurrency.Trim();

    private async Task<Dictionary<string, (decimal Price, string? Currency, string Source)>> ResolvePricesAsync(string? cardCode, int? priceList, DateOnly date,
        IReadOnlyCollection<string> codes, CancellationToken ct)
    {
        var result = new Dictionary<string, (decimal Price, string? Currency, string Source)>(StringComparer.OrdinalIgnoreCase);
        var distinctCodes = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctCodes.Count == 0)
        {
            return result;
        }

        if (!string.IsNullOrWhiteSpace(cardCode))
        {
            var special = await _priceLists.GetSpecialPriceQuotesAsync(cardCode.Trim(), distinctCodes, date.ToDateTime(TimeOnly.MinValue), ct);
            foreach (var (itemCode, quote) in special)
            {
                result[itemCode] = (quote.Price, quote.Currency, "special");
            }
        }

        var pending = distinctCodes.Where(c => !result.ContainsKey(c)).ToList();
        if (priceList is not null && pending.Count > 0)
        {
            var listPrices = await _priceLists.GetPriceQuotesAsync(pending, priceList.Value, ct);
            foreach (var (itemCode, quote) in listPrices)
            {
                result[itemCode] = (quote.Price, quote.Currency, "list");
            }
        }

        return result;
    }

    private async Task FillMissingPricesAsync(CancellationToken ct)
    {
        var missing = Input.Lines.Where(l => l.UnitPrice is null && !string.IsNullOrWhiteSpace(l.ItemCode)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var prices = await ResolvePricesAsync(Input.SupplierCardCode, Input.PriceListCode, Input.DocDate, missing.Select(l => l.ItemCode!).ToList(), ct);
        foreach (var line in missing)
        {
            if (prices.TryGetValue(line.ItemCode!.Trim(), out var resolved))
            {
                line.UnitPrice = resolved.Price;
                line.Currency = resolved.Currency;
            }
        }
    }

    /// <summary>
    /// Formulario personalizado (panel "Personalizar" de DocumentForm, ver form-layout.js):
    /// guarda los campos ocultos del usuario en la compañía activa, o con reset=true borra
    /// su versión y vuelve a la base del admin. Solo exige sesión -- es una preferencia
    /// visual, no una acción sobre el documento. IFormLayoutService se resuelve por
    /// RequestServices para no tocar el constructor de cada subtipo.
    /// </summary>
    public async Task<IActionResult> OnPostFormLayoutAsync([FromForm] string[]? hidden, [FromForm] bool reset, CancellationToken ct)
    {
        try
        {
            if (reset)
            {
                await FormLayouts.ResetForCurrentUserAsync(FormLayoutKey, ct);
            }
            else
            {
                await FormLayouts.SaveForCurrentUserAsync(FormLayoutKey, hidden ?? [], ct);
            }
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var layout = await FormLayouts.GetForCurrentUserAsync(FormLayoutKey, ct);
        return new JsonResult(new { layout.HiddenFields, layout.HasUserOverride });
    }

    // ---- Restricciones de líneas (stock / precio) -- ver IDocumentLineValidationService.
    // Siempre bloqueantes: al importar el Excel el editor de líneas llama a
    // OnPostValidateLinesAsync y marca cada línea; al crear, OnPostAsync vuelve a validar
    // en el servidor (no se salta editando a mano después de importar). Qué se valida lo
    // decide SIEMPRE el servidor según el motor/tipo, nunca el navegador.
    /// <summary>Compra SUMA stock -- no se valida disponible (mismo criterio que Importación Genérica). Solo precio.</summary>
    private const bool ValidatesStock = false;

    private const bool ValidatesPrice = true;

    private IDocumentLineValidationService LineValidator => HttpContext.RequestServices.GetRequiredService<IDocumentLineValidationService>();

    public async Task<IActionResult> OnPostValidateLinesAsync([FromBody] LineValidationInput input, CancellationToken ct)
    {
        if (!await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct))
        {
            return Forbid();
        }

        var checks = (input.Lines ?? [])
            .Where(l => !string.IsNullOrWhiteSpace(l.ItemCode))
            .Select(l => new DocumentLineCheck(l.Key, l.ItemCode!, l.Warehouse, l.Quantity ?? 0, ValidatesPrice ? l.UnitPrice : null, l.Currency))
            .ToList();
        var issues = await LineValidator.ValidateAsync(
            new DocumentLineValidationRequest(checks, ValidatesStock, ValidatesPrice, input.CardCode, input.PriceList, input.Date), ct);
        return new JsonResult(issues);
    }

    /// <summary>Valida las líneas ya filtradas de Input.Lines; agrega un error por problema (clave = campo de la línea).</summary>
    private async Task AddLineRestrictionErrorsAsync(CancellationToken ct)
    {
        var checks = Input.Lines
            .Select((l, i) => new DocumentLineCheck(i, l.ItemCode ?? string.Empty, l.WarehouseCode, l.Quantity ?? 0, l.UnitPrice, l.Currency))
            .ToList();
        var issues = await LineValidator.ValidateAsync(
            new DocumentLineValidationRequest(checks, ValidatesStock, ValidatesPrice, Input.SupplierCardCode, Input.PriceListCode, Input.DocDate), ct);
        foreach (var issue in issues)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{issue.Key}].{issue.Field}", $"Línea {issue.Key + 1}: {issue.Message}");
        }
    }

    public sealed class LineValidationInput
    {
        public string? CardCode { get; set; }
        public int? PriceList { get; set; }
        public DateOnly? Date { get; set; }
        public List<LineValidationRow>? Lines { get; set; }
    }

    public sealed class LineValidationRow
    {
        public int Key { get; set; }
        public string? ItemCode { get; set; }
        public string? Warehouse { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? Currency { get; set; }
    }

    // ---- Borradores (2026-10-20) -- ver IDocumentDraftService y
    // docs/superpowers/plans/2026-10-20-borradores-documentos.md. Guardar no valida nada
    // (un borrador puede estar incompleto); "Validar" corre las mismas reglas que crear +
    // una simulación contra SAP sin crear; "Enviar a SAP" es el mismo OnPostAsync con
    // DraftId (guarda, valida, crea y marca el borrador como enviado).
    private const string DraftEngine = DocumentDraftEngine.Purchase;

    private bool _validateOnly;

    [BindProperty]
    public Guid? DraftId { get; set; }

    [BindProperty]
    public string? DraftReason { get; set; }

    /// <summary>Borrador abierto (para el panel: creador, fechas, último error).</summary>
    public DocumentDraftDto? OpenDraft { get; private set; }

    private IDocumentDraftService Drafts => HttpContext.RequestServices.GetRequiredService<IDocumentDraftService>();

    private IDocumentSapMarkerService Markers => HttpContext.RequestServices.GetRequiredService<IDocumentSapMarkerService>();

    public async Task<IActionResult> OnPostSaveDraftAsync(CancellationToken ct)
    {
        IsNew = true;
        if (!await CanCreateDocumentAsync(ct))
        {
            return Forbid();
        }

        ModelState.Clear();
        try
        {
            DraftId = await SaveDraftAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return await PageWithErrorAsync(ex.Message, ct);
        }

        TempData["DocumentActionMessage"] = "Borrador guardado. Lo encontrás en el listado, pestaña Borradores.";
        TempData["DocumentActionMessageType"] = "alert-success";
        return RedirectToPage(new { id = NewId, draft = DraftId });
    }

    public Task<IActionResult> OnPostValidateDraftAsync(CancellationToken ct)
    {
        _validateOnly = true;
        return OnPostAsync(ct);
    }

    public async Task<IActionResult> OnPostDeleteDraftAsync(CancellationToken ct)
    {
        if (!await CanCreateDocumentAsync(ct))
        {
            return Forbid();
        }

        var listUrl = $"{Request.PathBase}{RouteBase}?view=borradores";
        if (DraftId is { } id)
        {
            try
            {
                await Drafts.DeleteAsync(id, listUrl, ct);
            }
            catch (InvalidOperationException ex)
            {
                IsNew = true;
                return await PageWithErrorAsync(ex.Message, ct);
            }
        }

        return Redirect(listUrl);
    }

    private async Task<bool> CanCreateDocumentAsync(CancellationToken ct) =>
        await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct) && await _documents.CanCreateAsync(Type, ct);

    private async Task<Guid> SaveDraftAsync(CancellationToken ct)
    {
        var reason = DraftReason;
        if (string.IsNullOrWhiteSpace(reason) && DraftId is { } existing)
        {
            reason = (await Drafts.GetAsync(existing, ct))?.Reason;
        }

        // Filas totalmente vacías no se guardan (las "+ Agregar línea" sin tocar).
        Input.Lines = Input.Lines.Where(l => !string.IsNullOrWhiteSpace(l.ItemCode) || !string.IsNullOrWhiteSpace(l.Description)).ToList();
        return await Drafts.SaveAsync(new DocumentDraftSaveRequest(
            DraftId, DraftEngine, Type.ToString(), reason ?? string.Empty, JsonSerializer.Serialize(Input),
            Input.SupplierCardCode, Input.SupplierReferenceNumber, Input.DocDate, Input.Lines.Count, Input.Lines.Sum(l => (l.Quantity ?? 0) * (l.UnitPrice ?? 0) * (1 - (l.DiscountPercent ?? 0) / 100m))), ct);
    }

    private async Task<IActionResult> SimulateAsync(PurchaseDocumentDto document, CancellationToken ct)
    {
        try
        {
            var result = await _documents.SimulateCreateAsync(Type, _currentUser.Username, document, ct);
            if (result.Outcome == SapSimulationOutcome.Valid)
            {
                TempData["DocumentActionMessage"] = "Validación OK: stock y precio correctos y SAP aceptaría el documento (no se creó nada).";
                TempData["DocumentActionMessageType"] = "alert-success";
            }
            else
            {
                ModelState.AddModelError(string.Empty, result.Outcome == SapSimulationOutcome.Rejected
                    ? $"SAP rechazaría el documento: {result.Message}"
                    : $"No se pudo confirmar con SAP: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo confirmar con SAP: {ex.Message}");
        }

        await LoadCatalogsAsync(ct);
        BuildDocumentViewModel();
        return Page();
    }

    private async Task<IActionResult> PageWithErrorAsync(string message, CancellationToken ct)
    {
        ModelState.AddModelError(string.Empty, message);
        await LoadCatalogsAsync(ct);
        BuildDocumentViewModel();
        return Page();
    }

    private const string FormLayoutKey = DocumentFormFieldCatalog.Purchase;

    private FormLayoutDto _formLayout = FormLayoutDto.Empty;

    private IFormLayoutService FormLayouts => HttpContext.RequestServices.GetRequiredService<IFormLayoutService>();

    private async Task LoadCatalogsAsync(CancellationToken ct)
    {
        _formLayout = await FormLayouts.GetForCurrentUserAsync(FormLayoutKey, ct);
        OpenDraft = IsNew && DraftId is { } openDraftId ? await Drafts.GetAsync(openDraftId, ct) : null;

        var series = await _series.ListAsync(_documents.GetSapObjectCode(Type).ToString(), ct: ct);
        Series = series.Select(s => new SelectListItem(s.SeriesName, s.SeriesCode.ToString())).ToList();

        var buyers = await _buyers.ListAsync(ct: ct);
        Buyers = buyers.Select(b => new SelectListItem(b.SalesEmployeeName, b.SalesEmployeeCode.ToString())).ToList();

        var priceLists = await _priceLists.ListAllAsync(ct);
        PriceLists = priceLists.Select(p => new SelectListItem(p.ListName, p.ListNum.ToString())).ToList();

        var currencies = await _currencies.ListAsync(ct: ct);
        Currencies = currencies.Select(c => new SelectListItem($"{c.CurrencyCode} — {c.CurrencyName}", c.CurrencyCode)).ToList();
        LocalCurrency = await _currencies.GetLocalCurrencyAsync(ct);
    }

    private void BuildDocumentViewModel()
    {
        Document = new DocumentFormViewModel
        {
            Title = IsNew ? $"Nuevo/a {DocumentName}" : $"{DocumentName} N° {DocNum}",
            ReadOnly = !IsNew,
            BackUrl = Url.IsLocalUrl(ReturnUrl) && ReturnUrl is not null ? ReturnUrl : Request.PathBase + RouteBase,
            StatusText = Status,
            StatusClass = Status == "Abierto" ? "bg-success" : "bg-secondary",
            Model = this,
            GeneralView = "~/Pages/Shared/_TabGeneralCompras.cshtml",
            ContentView = "~/Pages/Shared/_TabContentCompras.cshtml",
            DocEntry = DocEntry,
            CanClose = CanClose,
            CanCancel = CanCancel,
            FormKey = FormLayoutKey,
            FormLayout = _formLayout,
            Draft = IsNew
                ? new DocumentDraftPanel(DraftId, DraftReason ?? OpenDraft?.Reason, OpenDraft?.Status, OpenDraft?.CreatedByUsername,
                    OpenDraft?.CreatedAt, OpenDraft?.UpdatedByUsername, OpenDraft?.UpdatedAt, OpenDraft?.ErrorMessage)
                : null,
        };

        // Consumido por _ModuloBackLink.cshtml (breadcrumb del layout) -- ver el
        // comentario equivalente en DetailGenericSalesDocumentModelBase.cs.
        ViewData["ModuleBreadcrumbBackUrl"] = Document.BackUrl;
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Elegí un proveedor.")]
        [Display(Name = "Proveedor")]
        public string SupplierCardCode { get; set; } = string.Empty;

        [Display(Name = "Comentarios")]
        public string? Comments { get; set; }

        [Display(Name = "Fecha de documento")]
        public DateOnly DocDate { get; set; }

        [Display(Name = "Fecha de vencimiento")]
        public DateOnly DocDueDate { get; set; }

        [Display(Name = "Fecha de contabilización")]
        public DateOnly TaxDate { get; set; }

        [Display(Name = "N° referencia proveedor")]
        public string? SupplierReferenceNumber { get; set; }

        [Display(Name = "Serie")]
        public int? Series { get; set; }

        /// <summary>OSLP.SlpCode -- por defecto el del proveedor (OCRD.SlpCode), modificable.</summary>
        [Display(Name = "Comprador")]
        public int? BuyerCode { get; set; }

        /// <summary>OPLN.ListNum -- solo para resolver el precio de las líneas; por defecto la del proveedor, modificable.</summary>
        [Display(Name = "Lista de precio")]
        public int? PriceListCode { get; set; }

        /// <summary>OCRN.CurrCode -- moneda en que se digita el documento; por defecto la del proveedor (o la local), modificable.</summary>
        [Display(Name = "Moneda")]
        public string? DocCurrency { get; set; }

        [Display(Name = "Tipo de línea")]
        public DocumentLineType LineType { get; set; } = DocumentLineType.Item;

        public List<LineInput> Lines { get; set; } = [new()];
    }

    public sealed class LineInput
    {
        public string? ItemCode { get; set; }
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? DiscountPercent { get; set; }
        public string? WarehouseCode { get; set; }
        public string? AccountCode { get; set; }
        public string? CostCenterCode { get; set; }
        public string? CostCenterCode2 { get; set; }
        public string? CostCenterCode3 { get; set; }

        /// <summary>Moneda de UnitPrice -- ver LineInput.Currency en DetailGenericSalesDocumentModelBase (Modulo.Ventas).</summary>
        public string? Currency { get; set; }
    }
}
