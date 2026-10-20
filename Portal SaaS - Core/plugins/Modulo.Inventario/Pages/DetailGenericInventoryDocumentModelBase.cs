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
    private readonly ISeriesCatalogService _series;
    private readonly ICustomerCatalogService _customers;

    protected DetailGenericInventoryDocumentModelBase(
        IInventoryDocumentService documents,
        ICurrentUserContext currentUser,
        IWarehouseCatalogService warehouses,
        IItemCatalogService items,
        ISeriesCatalogService series,
        ICustomerCatalogService customers)
    {
        _documents = documents;
        _currentUser = currentUser;
        _warehouses = warehouses;
        _items = items;
        _series = series;
        _customers = customers;
    }

    protected abstract InventoryDocumentType Type { get; }
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
    public string? Status { get; private set; }

    /// <summary>Ver DocumentFormViewModel.CanClose para el criterio completo.</summary>
    public bool CanClose { get; private set; }

    /// <summary>Ver DocumentFormViewModel.CanCancel para el criterio completo.</summary>
    public bool CanCancel { get; private set; }

    /// <summary>Solo-lectura (documento existente) -- mismo criterio que SalesDocumentDto.CustomerName, ver OnGetSearchCustomersAsync.</summary>
    public string? CustomerName { get; private set; }

    public List<SelectListItem> Series { get; private set; } = [];

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
                Series = null,
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
            Status = document.Status;
            CustomerName = document.BusinessPartnerName;
            CanClose = Status != "Cerrado"
                && await _documents.CanCreateAsync(Type, ct)
                && await _currentUser.HasActionAsync(MenuCode, PortalActions.Delete, ct);
            CanCancel = CanClose && _documents.SupportsCancel(Type);

            Input = new InputModel
            {
                Comments = document.Comments,
                DocDate = document.DocDate,
                Series = document.Series,
                CustomerCardCode = document.BusinessPartnerCardCode,
                CustomerReferenceNumber = document.CustomerReferenceNumber,
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

        await AddLineRestrictionErrorsAsync(ct);
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
                ToWarehouseCode: line.ToWarehouseCode!)).ToList(),
            Series: Input.Series,
            BusinessPartnerCardCode: string.IsNullOrWhiteSpace(Input.CustomerCardCode) ? null : Input.CustomerCardCode,
            CustomerReferenceNumber: Input.CustomerReferenceNumber);

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
    /// Cierra el documento en SAP (ver IInventoryDocumentService.CloseAsync) -- validado
    /// en el servidor con el mismo gate que habilita el botón (CanCreateAsync +
    /// PortalActions.Delete), portado de OnPostCerrarAsync (DetalleGenericoVentaModelBase,
    /// referencia-original/PortalSAP_v2).
    /// </summary>
    public Task<IActionResult> OnPostCloseAsync(string id, CancellationToken ct) =>
        ExecuteStatusActionAsync(id, docEntry => _documents.CloseAsync(Type, docEntry, ct), "cerrado", requireSupportsCancel: false, ct);

    /// <summary>
    /// Cancela el documento en SAP (ver IInventoryDocumentService.CancelAsync) -- mismo
    /// gate que OnPostCloseAsync, MÁS SupportsCancel(Type) (revalidado server-side, no
    /// solo ocultando el botón): Service Layer rechaza "/Cancel" sobre documentos de
    /// intención (ej. Solicitud de Traslado) con "The requested action is not supported
    /// for this object" -- error real confirmado 2026-09-04.
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

    /// <summary>Ver el comentario completo en OnGetSearchWarehousesAsync (DetailGenericSalesDocumentModelBase, Modulo.Ventas), mismo criterio.</summary>
    public async Task<JsonResult> OnGetSearchWarehousesAsync(string text, CancellationToken ct)
    {
        var warehouses = await _warehouses.ListAsync(text, 30, ct);
        return new JsonResult(warehouses.Select(w => new { w.WarehouseCode, w.WarehouseName }));
    }

    /// <summary>Búsqueda en vivo de socio de negocio -- mismo criterio y mismo catálogo que OnGetSearchCustomersAsync (DetailGenericSalesDocumentModelBase, Modulo.Ventas).</summary>
    public async Task<JsonResult> OnGetSearchCustomersAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonResult(Array.Empty<object>());
        }

        var customers = await _customers.ListAsync(new CustomerFilter(text, Limit: 30), ct);
        return new JsonResult(customers.Select(c => new { c.CardCode, c.CardName }));
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
    /// <summary>Traslado: stock contra el almacén ORIGEN (el que descuenta). Sin precio por línea -- no aplica.</summary>
    private const bool ValidatesStock = true;

    private const bool ValidatesPrice = false;

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
            .Select((l, i) => new DocumentLineCheck(i, l.ItemCode ?? string.Empty, l.FromWarehouseCode, l.Quantity ?? 0, null, null))
            .ToList();
        var issues = await LineValidator.ValidateAsync(
            new DocumentLineValidationRequest(checks, ValidatesStock, ValidatesPrice, null, null, Input.DocDate), ct);
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
    private const string DraftEngine = DocumentDraftEngine.Inventory;

    private bool _validateOnly;

    [BindProperty]
    public Guid? DraftId { get; set; }

    [BindProperty]
    public string? DraftReason { get; set; }

    /// <summary>Borrador abierto (para el panel: creador, fechas, último error).</summary>
    public DocumentDraftDto? OpenDraft { get; private set; }

    private IDocumentDraftService Drafts => HttpContext.RequestServices.GetRequiredService<IDocumentDraftService>();

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
            Input.CustomerCardCode, Input.CustomerReferenceNumber, Input.DocDate, Input.Lines.Count, (decimal?)null), ct);
    }

    private async Task<IActionResult> SimulateAsync(InventoryDocumentDto document, CancellationToken ct)
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

    private const string FormLayoutKey = DocumentFormFieldCatalog.Inventory;

    private FormLayoutDto _formLayout = FormLayoutDto.Empty;

    private IFormLayoutService FormLayouts => HttpContext.RequestServices.GetRequiredService<IFormLayoutService>();

    private async Task LoadCatalogsAsync(CancellationToken ct)
    {
        _formLayout = await FormLayouts.GetForCurrentUserAsync(FormLayoutKey, ct);
        OpenDraft = IsNew && DraftId is { } openDraftId ? await Drafts.GetAsync(openDraftId, ct) : null;

        var series = await _series.ListAsync(_documents.GetSapObjectCode(Type).ToString(), ct: ct);
        Series = series.Select(s => new SelectListItem(s.SeriesName, s.SeriesCode.ToString())).ToList();
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
            GeneralView = "~/Pages/Shared/_TabGeneralInventario.cshtml",
            ContentView = "~/Pages/Shared/_TabContentInventario.cshtml",
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
        [Display(Name = "Comentarios")]
        public string? Comments { get; set; }

        [Display(Name = "Fecha de documento")]
        public DateOnly DocDate { get; set; }

        [Display(Name = "Serie")]
        public int? Series { get; set; }

        [Display(Name = "Socio de negocio")]
        public string? CustomerCardCode { get; set; }

        [Display(Name = "N° referencia")]
        public string? CustomerReferenceNumber { get; set; }

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
