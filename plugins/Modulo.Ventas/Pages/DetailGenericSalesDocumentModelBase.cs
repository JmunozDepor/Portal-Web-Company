using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages;

/// <summary>
/// Crear/ver un documento de venta genérico -- solo creación, sin
/// ISalesDocumentService.UpdateAsync todavía (digitación directa: se crea una vez,
/// después es de solo lectura vía el portal; editarlo de verdad se hace directo en
/// SAP). Generaliza el antiguo DetailModel de SalesOrders/ (ahí vivía toda esta lógica
/// hardcodeada a un solo tipo) -- portado de DetalleGenericoVentaModelBase en
/// referencia-original/PortalSAP_v2: cada subtipo concreto (SalesOrders/DetailModel,
/// CreditNotes/DetailModel, ...) solo declara Type/DocumentName/RouteBase.
///
/// REGLA DURA (ver CLAUDE.md): todo nace del documento padre. OnGetAsync/OnPostAsync/
/// OnGetSearchItemsAsync NO son virtual a propósito -- ningún subtipo puede modificar
/// cómo se crea/valida/lee un documento, solo puede identificarse. Cualquier tipo de
/// documento de venta nuevo se agrega acá (SalesDocumentTypeCatalog + este subtipo),
/// nunca reimplementando esta clase.
/// </summary>
[Authorize]
public abstract class DetailGenericSalesDocumentModelBase : PageModel
{
    private const string NewId = "nuevo";

    private readonly ISalesDocumentService _documents;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICustomerCatalogService _customers;
    private readonly IWarehouseCatalogService _warehouses;
    private readonly ISalesEmployeeCatalogService _salesEmployees;
    private readonly IItemCatalogService _items;
    private readonly IGeneralLedgerAccountCatalogService _accounts;
    private readonly ICostCenterCatalogService _costCenters;
    private readonly ISeriesCatalogService _series;
    private readonly IShippingMethodCatalogService _shippingMethods;
    private readonly IPaymentTermsCatalogService _paymentTerms;

    protected DetailGenericSalesDocumentModelBase(
        ISalesDocumentService documents,
        ICurrentUserContext currentUser,
        ICustomerCatalogService customers,
        IWarehouseCatalogService warehouses,
        ISalesEmployeeCatalogService salesEmployees,
        IItemCatalogService items,
        IGeneralLedgerAccountCatalogService accounts,
        ICostCenterCatalogService costCenters,
        ISeriesCatalogService series,
        IShippingMethodCatalogService shippingMethods,
        IPaymentTermsCatalogService paymentTerms)
    {
        _documents = documents;
        _currentUser = currentUser;
        _customers = customers;
        _warehouses = warehouses;
        _salesEmployees = salesEmployees;
        _items = items;
        _accounts = accounts;
        _costCenters = costCenters;
        _series = series;
        _shippingMethods = shippingMethods;
        _paymentTerms = paymentTerms;
    }

    protected abstract SalesDocumentType Type { get; }
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

    /// <summary>
    /// Nombre del cliente ya elegido -- solo para MOSTRAR junto al código en modo
    /// solo-lectura (documento existente). Viene de SalesDocumentDto.CustomerName
    /// (ya lo trae SAP en la cabecera), NUNCA de precargar el catálogo completo de
    /// clientes -- ver el comentario de OnGetSearchCustomersAsync sobre por qué
    /// Cliente dejó de tener un &lt;select&gt; con todo OCRD.
    /// </summary>
    public string? CustomerName { get; private set; }

    public List<SelectListItem> SalesEmployees { get; private set; } = [];
    public List<SelectListItem> Series { get; private set; } = [];
    public List<SelectListItem> ShippingMethods { get; private set; } = [];
    public List<SelectListItem> PaymentTerms { get; private set; } = [];

    /// <summary>
    /// "Resumen total" del tab Contenido -- portado tal cual de
    /// DetalleGenericoVentaModelBase (referencia-original/PortalSAP_v2): subtotal/
    /// descuento se calculan de las líneas del formulario (nunca de SAP), el impuesto
    /// sale como "lo que falta" entre ese subtotal y el DocTotal real de SAP -- por
    /// eso da 0 mientras se está creando un documento nuevo (sin DocTotal todavía, ver
    /// DocumentTotal) y solo se ve un valor real una vez que el documento ya existe en
    /// SAP. GastosAdicionales queda fijo en 0 -- el original tampoco lo calcula (sin
    /// tracking de gastos adicionales en ningún lado).
    /// </summary>
    public decimal TotalBeforeDiscount => Input.Lines.Sum(l => (l.Quantity ?? 0) * (l.UnitPrice ?? 0));
    public decimal Discount => Input.Lines.Sum(l => (l.Quantity ?? 0) * (l.UnitPrice ?? 0) * (l.DiscountPercent ?? 0) / 100m);
    public decimal AdditionalExpenses => 0m;
    public decimal DocumentTotal => DocTotal ?? (TotalBeforeDiscount - Discount);
    public decimal Tax => DocumentTotal - (TotalBeforeDiscount - Discount) - AdditionalExpenses;

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
                DocDueDate = DateOnly.FromDateTime(DateTime.Today),
                TaxDate = DateOnly.FromDateTime(DateTime.Today),
                LineType = DocumentLineType.Item,
                Series = null,
                ShippingMethodCode = null,
                PaymentTermsGroupCode = null,
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
            DocTotal = document.DocTotal;
            Status = document.Status;
            CustomerName = document.CustomerName;

            Input = new InputModel
            {
                CustomerCardCode = document.CustomerCardCode,
                SalesEmployeeCode = document.SalesEmployeeCode,
                Comments = document.Comments,
                DocDate = document.DocDate,
                DocDueDate = document.DocDueDate,
                TaxDate = document.TaxDate,
                CustomerReferenceNumber = document.CustomerReferenceNumber,
                LineType = document.Lines.Count > 0 ? document.Lines[0].Type : DocumentLineType.Item,
                Series = document.Series,
                ShippingMethodCode = document.ShippingMethodCode,
                PaymentTermsGroupCode = document.PaymentTermsGroupCode,
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

        var isService = Input.LineType == DocumentLineType.Service;

        // Un documento de Servicio identifica sus filas con datos por Descripción (no
        // hay Código de Artículo); Artículo sigue igual que antes -- mismo criterio
        // "TieneDatos" de la referencia.
        Input.Lines = Input.Lines
            .Where(line => isService ? !string.IsNullOrWhiteSpace(line.Description) : !string.IsNullOrWhiteSpace(line.ItemCode))
            .ToList();

        if (Input.Lines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, $"Agregá al menos una línea antes de crear {DocumentNameLowerWithArticle()}.");
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

        var document = new SalesDocumentDto(
            CustomerCardCode: Input.CustomerCardCode,
            CustomerName: null,
            SalesEmployeeCode: Input.SalesEmployeeCode,
            Comments: Input.Comments,
            DocDate: Input.DocDate,
            DocDueDate: Input.DocDueDate,
            TaxDate: Input.TaxDate,
            CustomerReferenceNumber: Input.CustomerReferenceNumber,
            Lines: Input.Lines.Select(line => new SalesDocumentLineDto(
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
                CostCenterCode3: isService ? line.CostCenterCode3 : null)).ToList(),
            Series: Input.Series,
            ShippingMethodCode: Input.ShippingMethodCode,
            PaymentTermsGroupCode: Input.PaymentTermsGroupCode);

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

    /// <summary>
    /// Resuelve en una sola consulta los códigos de artículo que vinieron de una
    /// importación Excel de líneas (ver document-lines-editor.js) -- mismo mecanismo
    /// que usa Modulo.ImportacionGenerica para cruzar todos los ItemCode distintos de
    /// un archivo de una vez (IItemCatalogService.GetByCodesAsync), en vez de dejar que
    /// el usuario descubra un código inválido recién al enviar el formulario completo.
    /// Devuelve solo los códigos que SÍ existen -- el cliente calcula la diferencia
    /// contra lo que importó.
    /// </summary>
    public async Task<JsonResult> OnGetValidateItemCodesAsync(string[] codes, CancellationToken ct)
    {
        var distinctCodes = (codes ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var items = await _items.GetByCodesAsync(distinctCodes, ct);
        return new JsonResult(items.Select(i => new { i.ItemCode, i.ItemName }));
    }

    /// <summary>
    /// Búsqueda en vivo del cliente -- mismo modelo que Artículo (LIKE acotado con
    /// LIMIT + debounce/mínimo de caracteres del lado cliente, ver catalog-search.js).
    /// Antes esta pantalla precargaba TODO OCRD (CardType='C') en un &lt;select&gt; en
    /// cada carga del formulario -- mismo orden de magnitud que Artículo (cientos de
    /// miles de filas posibles), pero peor: se pagaba en CADA apertura del documento,
    /// no solo al tipear. Regla dura de este proyecto (ver CLAUDE.md): todo catálogo
    /// cuya tabla SAP puede crecer sin tope conocido se busca en vivo, nunca se
    /// precarga completo.
    /// </summary>
    public async Task<JsonResult> OnGetSearchCustomersAsync(string text, CancellationToken ct)
    {
        // Sin texto, CustomerCatalogService.ListAsync serviría el catálogo COMPLETO
        // (mismo criterio de CatalogSqlHelper que ya usan Almacén/Vendedor/etc. para
        // dropdowns chicos) -- acá el corte explícito es obligatorio, exactamente lo
        // que este cambio busca evitar. Mismo guard que ItemCatalogService.SearchAsync.
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonResult(Array.Empty<object>());
        }

        var customers = await _customers.ListAsync(new CustomerFilter(text, Limit: 30), ct);
        return new JsonResult(customers.Select(c => new { c.CardCode, c.CardName }));
    }

    /// <summary>
    /// Búsqueda en vivo del almacén por línea -- buscador en vivo (mismo mecanismo que
    /// Artículo/Cliente/Proveedor), pero SIN el guard de texto vacío -- Almacén/Cuenta
    /// Mayor/Centro de Costos son catálogos chicos (decisión explícita del dueño del
    /// proyecto, 27 jul 2026), así que el cliente (catalog-search.js, minChars: 0)
    /// precarga el desplegable completo con una búsqueda de texto vacío al cablear el
    /// campo -- acá SÍ corresponde servir el listado completo sin filtro (ListAsync
    /// con searchText vacío, ver CatalogSqlHelper), a diferencia de Artículo/Cliente/
    /// Proveedor donde texto vacío siempre devuelve [] (catálogos grandes, jamás se
    /// precargan completos).
    /// </summary>
    public async Task<JsonResult> OnGetSearchWarehousesAsync(string text, CancellationToken ct)
    {
        var warehouses = await _warehouses.ListAsync(text, 30, ct);
        return new JsonResult(warehouses.Select(w => new { w.WarehouseCode, w.WarehouseName }));
    }

    /// <summary>Búsqueda en vivo de Cuenta Mayor (línea de Servicio) -- ver OnGetSearchWarehousesAsync.</summary>
    public async Task<JsonResult> OnGetSearchAccountsAsync(string text, CancellationToken ct)
    {
        var accounts = await _accounts.ListAsync(text, 30, ct);
        return new JsonResult(accounts.Select(a => new { a.AccountCode, a.AccountName }));
    }

    /// <summary>Búsqueda en vivo de Centro de Costos, Dimensión 1 (línea de Servicio) -- ver OnGetSearchWarehousesAsync.</summary>
    public async Task<JsonResult> OnGetSearchCostCentersAsync(string text, CancellationToken ct)
    {
        var costCenters = await _costCenters.ListAsync(text, 30, ct);
        return new JsonResult(costCenters.Select(c => new { c.Code, c.Name }));
    }

    /// <summary>Búsqueda en vivo de Marca -- Dimensión 2 (DimCode 2), opcional en línea de Servicio.</summary>
    public async Task<JsonResult> OnGetSearchCostCenters2Async(string text, CancellationToken ct)
    {
        var costCenters = await _costCenters.ListByDimensionAsync(2, text, 30, ct);
        return new JsonResult(costCenters.Select(c => new { c.Code, c.Name }));
    }

    /// <summary>Búsqueda en vivo de Tipo de Gasto -- Dimensión 3 (DimCode 5), opcional en línea de Servicio.</summary>
    public async Task<JsonResult> OnGetSearchCostCenters3Async(string text, CancellationToken ct)
    {
        var costCenters = await _costCenters.ListByDimensionAsync(5, text, 30, ct);
        return new JsonResult(costCenters.Select(c => new { c.Code, c.Name }));
    }

    private async Task LoadCatalogsAsync(CancellationToken ct)
    {
        var salesEmployees = await _salesEmployees.ListAsync(ct: ct);
        SalesEmployees = salesEmployees.Select(s => new SelectListItem(s.SalesEmployeeName, s.SalesEmployeeCode.ToString())).ToList();

        var series = await _series.ListAsync(_documents.GetSapObjectCode(Type).ToString(), ct: ct);
        Series = series.Select(s => new SelectListItem(s.SeriesName, s.SeriesCode.ToString())).ToList();

        var shippingMethods = await _shippingMethods.ListAsync(ct: ct);
        ShippingMethods = shippingMethods.Select(s => new SelectListItem(s.ShippingMethodName, s.ShippingMethodCode.ToString())).ToList();

        var paymentTerms = await _paymentTerms.ListAsync(ct: ct);
        PaymentTerms = paymentTerms.Select(p => new SelectListItem(p.PaymentTermsName, p.PaymentTermsCode.ToString())).ToList();
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
            GeneralView = "~/Pages/Shared/_TabGeneralVentas.cshtml",
            ContentView = "~/Pages/Shared/_TabContentVentas.cshtml",
            LogisticsView = "~/Pages/Shared/_TabLogisticaVentas.cshtml",
            AccountingView = "~/Pages/Shared/_TabFinanzasVentas.cshtml",
            AccountingTitle = "Finanzas",
        };
    }

    private string DocumentNameLowerWithArticle() => DocumentName.ToLowerInvariant();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Elegí un cliente.")]
        [Display(Name = "Cliente")]
        public string CustomerCardCode { get; set; } = string.Empty;

        [Display(Name = "Vendedor")]
        public int? SalesEmployeeCode { get; set; }

        [Display(Name = "Comentarios")]
        public string? Comments { get; set; }

        [Display(Name = "Fecha de documento")]
        public DateOnly DocDate { get; set; }

        [Display(Name = "Fecha de vencimiento")]
        public DateOnly DocDueDate { get; set; }

        [Display(Name = "Fecha de contabilización")]
        public DateOnly TaxDate { get; set; }

        [Display(Name = "N° referencia cliente")]
        public string? CustomerReferenceNumber { get; set; }

        [Display(Name = "Serie")]
        public int? Series { get; set; }

        [Display(Name = "Método de envío")]
        public int? ShippingMethodCode { get; set; }

        [Display(Name = "Condición de pago")]
        public int? PaymentTermsGroupCode { get; set; }

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
    }
}
