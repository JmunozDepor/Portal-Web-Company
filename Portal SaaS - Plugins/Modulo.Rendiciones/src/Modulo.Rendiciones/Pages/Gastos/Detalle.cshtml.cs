using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Gastos;

/// <summary>
/// Alta/edición de un gasto suelto (digitación manual). El import masivo con OCR (hasta
/// 5 PDF o foto) vive en Pages/Gastos/Importar. Solo se puede editar/eliminar mientras
/// el gasto sigue Loose -- una vez que un informe lo agrupa, hay que desvincularlo
/// desde el informe primero.
/// </summary>
public sealed class DetalleModel : RendicionesRendidorPageModelBase
{
    private readonly IExpenseService _expenses;
    private readonly IExpenseTypeService _expenseTypes;
    private readonly IDocumentTypeService _documentTypes;
    private readonly IExpensePolicyService _policies;
    private readonly IExpenseApprovalGroupService _approvalGroups;
    private readonly IUserCostCenterService _userCostCenters;
    private readonly IAttachmentStorageService _attachments;
    private readonly IRoutingService _routing;
    private readonly ISupplierHintService _supplierHints;
    private readonly IAmountFormatService _amountFormat;
    private readonly ICityService _cities;
    private readonly IPolicyProfileService _policyProfiles;
    private readonly IWeeklyExpenseLimitService _weeklyLimits;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public DetalleModel(IExpenseService expenses, IExpenseTypeService expenseTypes, IDocumentTypeService documentTypes,
        IExpensePolicyService policies, IExpenseApprovalGroupService approvalGroups, IUserCostCenterService userCostCenters,
        IAttachmentStorageService attachments, IRoutingService routing, ISupplierHintService supplierHints,
        IAmountFormatService amountFormat, ICityService cities, IPolicyProfileService policyProfiles,
        IWeeklyExpenseLimitService weeklyLimits, IRendicionesUserRoleService roles, ICurrentUserContext currentUser,
        ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _expenses = expenses;
        _expenseTypes = expenseTypes;
        _documentTypes = documentTypes;
        _policies = policies;
        _approvalGroups = approvalGroups;
        _userCostCenters = userCostCenters;
        _attachments = attachments;
        _routing = routing;
        _supplierHints = supplierHints;
        _amountFormat = amountFormat;
        _cities = cities;
        _policyProfiles = policyProfiles;
        _weeklyLimits = weeklyLimits;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    /// <summary>Nombre de la categoría que la memoria del proveedor propone, si el gasto todavía no tiene una elegida. Null = no hay sugerencia.</summary>
    public string? SuggestedCategoryName { get; private set; }
    public long? SuggestedCategoryId { get; private set; }

    public bool IsNew { get; private set; } = true;
    public ExpenseReportLine? Expense { get; private set; }

    /// <summary>"Sin política" / "Tope N" / "Sin tope" de la política REAL aplicable a este
    /// gasto (misma resolución que ExpensePolicyService.ValidateAsync, ver
    /// ResolvePolicyLabelAsync) -- mismo criterio que Pages/Gastos/Index.PolicyLabelAsync y
    /// Pages/Informes/Index.PolicyLabelAsync (I5, revisión final 2026-10-29).</summary>
    public string? PolicyLabel { get; private set; }

    /// <summary>Advertencias de tope semanal (Task 8 / Decisión 6 del spec) para el rendidor
    /// actual, sobre la semana de la fecha del gasto que se está digitando/editando -- antes
    /// solo se mostraban en Informes/Detalle al auditor, nunca acá (I3, revisión final
    /// 2026-10-29). Puramente informativo, no bloquea nada.</summary>
    public IReadOnlyList<WeeklyLimitWarning> WeeklyWarnings { get; private set; } = Array.Empty<WeeklyLimitWarning>();

    /// <summary>
    /// "Política" en el sentido amplio (grupo de aprobación + centros de costo del
    /// usuario actual) -- informativa, NO se persiste en el gasto. La fijación real ya
    /// existe en el modelo: ExpenseReport.ExpenseApprovalGroupId/CostCenterCode se
    /// congelan solos al agrupar gastos sueltos en un informe (ver
    /// RendicionesDbContext, ExpenseReport ya tiene esos dos campos) -- agregar una
    /// copia más en ExpenseReportLine sería una migración nueva para duplicar un dato
    /// que el modelo ya fija un paso después, en el lugar correcto. Acá solo se
    /// muestra "bajo qué política estás hoy" para que el rendidor sepa qué esperar,
    /// mismo criterio que "1 grupo de aprobación por usuario" ya asumido en
    /// IExpenseApprovalGroupService.GetUserGroupAsync.
    /// </summary>
    public string ApprovalGroupSummary { get; private set; } = "Sin grupo de aprobación asignado (autoaprueba al enviar)";

    public string CostCenterSummary { get; private set; } = "Sin restricción (todos los centros de costo)";

    public IReadOnlyList<ExpenseType> ExpenseTypes { get; private set; } = Array.Empty<ExpenseType>();
    public IReadOnlyList<DocumentType> DocumentTypes { get; private set; } = Array.Empty<DocumentType>();
    public IReadOnlyList<City> Cities { get; private set; } = Array.Empty<City>();

    /// <summary>ExpenseTypeIds que tienen al menos una política activa acotada a una ciudad
    /// concreta (CityId != null) -- determina si el combo "Ciudad" se muestra en el
    /// formulario para el tipo elegido (Task 10). Se calcula una sola vez en
    /// LoadCatalogsAsync.</summary>
    public IReadOnlySet<long> ExpenseTypeIdsWithCityPolicy { get; private set; } = new HashSet<long>();

    [BindProperty]
    public ExpenseInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(long? id, CancellationToken ct)
    {
        await LoadCatalogsAsync(ct);
        await ResolvePolicySummaryAsync(ct);

        if (id is null)
        {
            await ResolveWeeklyWarningsAsync(Input.Date, ct);
            return Page();
        }

        var expense = await _expenses.GetAsync(id.Value, _currentCompany.CompanyId, ct);
        if (expense is null || expense.UserId != _currentUser.UserId)
            return NotFound();

        IsNew = false;
        Expense = expense;
        await ResolvePolicyLabelAsync(expense, ct);
        await ResolveSuggestedCategoryAsync(expense, ct);

        // La preferencia "sin decimales" es por compañía (RendicionesSettings.ShowAmountDecimals,
        // ver IAmountFormatService) -- si está apagada, los montos se precargan redondeados al
        // peso en el formulario de edición, no solo en las vistas de solo lectura. El CLP no
        // tiene centavos reales de por sí, así que no se pierde precisión de negocio.
        var showDecimals = await _amountFormat.ShowDecimalsAsync(ct);
        decimal Redondear(decimal monto) =>
            showDecimals ? monto : Math.Round(monto, 0, MidpointRounding.AwayFromZero);
        decimal? RedondearNullable(decimal? monto) => monto is { } m ? Redondear(m) : null;

        Input = new ExpenseInput
        {
            ExpenseTypeId = expense.ExpenseTypeId ?? 0,
            DocumentTypeId = expense.DocumentTypeId,
            Date = expense.Date.DateTime,
            TransactionTime = expense.TransactionTime,
            Amount = Redondear(expense.Amount),
            TaxAmount = RedondearNullable(expense.TaxAmount),
            NetAmount = RedondearNullable(expense.NetAmount),
            ExemptAmount = RedondearNullable(expense.ExemptAmount),
            Currency = expense.Currency,
            DocumentNumber = expense.DocumentNumber,
            SupplierTaxId = expense.SupplierTaxId,
            SupplierName = expense.SupplierName,
            Notes = expense.Notes,
            Origin = expense.Origin,
            Destination = expense.Destination,
            CityId = expense.CityId,
            Quantity = expense.Quantity,
        };
        await ResolveWeeklyWarningsAsync(Input.Date, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long? id, CancellationToken ct)
    {
        await LoadCatalogsAsync(ct);
        await ResolvePolicySummaryAsync(ct);
        await ResolveWeeklyWarningsAsync(Input.Date, ct);
        IsNew = id is null;

        if (id is { } existingId)
            Expense = await _expenses.GetAsync(existingId, _currentCompany.CompanyId, ct);

        // [Required] sobre un long no-nullable NO valida nada en ASP.NET Core -- un
        // value type nunca se ve "faltante" para el binder, así que Input.ExpenseTypeId
        // en 0 (categoría no elegida, o perdida en el binding) pasaba ModelState.IsValid
        // igual. Validación explícita, mismo criterio que Amount unas líneas más abajo.
        if (Input.ExpenseTypeId <= 0)
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ExpenseTypeId)}", "Elegí una categoría.");

        var selectedExpenseType = ExpenseTypes.FirstOrDefault(t => t.Id == Input.ExpenseTypeId);
        var isMileage = selectedExpenseType?.IsMileage == true;
        if (isMileage)
        {
            if (string.IsNullOrWhiteSpace(Input.Origin))
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Origin)}", "Ingresá el origen.");
            if (string.IsNullOrWhiteSpace(Input.Destination))
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Destination)}", "Ingresá el destino.");
            ModelState.Remove($"{nameof(Input)}.{nameof(Input.Amount)}"); // se recalcula server-side, ver ExpenseService
        }
        else if (Input.Amount <= 0)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Amount)}", "El monto debe ser mayor a 0.");
        }

        if (!ModelState.IsValid)
            return Page();

        try
        {
            long? receiptId = Expense?.ExpenseReceiptId;
            if (Input.Receipt is { Length: > 0 } file)
                receiptId = await SaveReceiptAsync(file, ct);

            var data = new ExpenseReportLine
            {
                CompanyId = _currentCompany.CompanyId,
                UserId = _currentUser.UserId,
                ExpenseTypeId = Input.ExpenseTypeId,
                DocumentTypeId = Input.DocumentTypeId,
                Date = Input.Date,
                TransactionTime = Input.TransactionTime,
                Amount = Input.Amount,
                TaxAmount = Input.TaxAmount,
                NetAmount = Input.NetAmount,
                ExemptAmount = Input.ExemptAmount,
                Currency = Input.Currency,
                DocumentNumber = Input.DocumentNumber,
                SupplierTaxId = Input.SupplierTaxId,
                SupplierName = Input.SupplierName,
                Notes = Input.Notes,
                Origin = isMileage ? Input.Origin : null,
                Destination = isMileage ? Input.Destination : null,
                ExpenseReceiptId = receiptId,
                CityId = Input.CityId,
                Quantity = Input.Quantity,
            };

            var warnings = id is null
                ? (await _expenses.CreateAsync(data, ct)).Warnings
                : await _expenses.UpdateAsync(id.Value, _currentCompany.CompanyId, data, ct);

            SuccessMessage = "Gasto guardado.";
            if (warnings.Count > 0)
                WarningMessage = string.Join(" ", warnings);

            return RedirectToPage("./Index");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return Page();
        }
    }

    /// <summary>
    /// Vista previa AJAX para el botón "Calcular ruta" -- el cálculo real y autoritativo
    /// (el que determina el Amount que se guarda) se vuelve a hacer server-side en
    /// ExpenseService al guardar, este handler nunca persiste nada.
    /// </summary>
    public async Task<IActionResult> OnPostPrevisualizarRutaAsync(long expenseTypeId, string? origen, string? destino, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(destino))
            return new JsonResult(new { ok = false, error = "Ingresá origen y destino." });

        var expenseType = (await _expenseTypes.ListActiveAsync(_currentCompany.CompanyId, ct)).FirstOrDefault(t => t.Id == expenseTypeId);
        if (expenseType is not { IsMileage: true })
            return new JsonResult(new { ok = false, error = "Este tipo de gasto no usa cálculo de kilometraje." });

        if (expenseType.RatePerKm is not { } rate || rate <= 0)
            return new JsonResult(new { ok = false, error = $"'{expenseType.Name}' no tiene tarifa por km configurada." });

        var result = await _routing.CalculateDistanceAsync(_currentCompany.CompanyId, origen, destino, ct);
        if (result.Error is not null || result.DistanceKm is not { } km)
            return new JsonResult(new { ok = false, error = result.Error ?? "No se pudo calcular la ruta." });

        return new JsonResult(new { ok = true, distanciaKm = km, monto = Math.Round(km * rate, 0) });
    }

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        try
        {
            await _expenses.DeleteAsync(id, _currentCompany.CompanyId, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage("./Detalle", new { id });
        }

        return RedirectToPage("./Index");
    }

    /// <summary>Botón "Eliminar" del visor de comprobante -- saca el archivo del gasto sin borrar
    /// el gasto en sí (a diferencia de OnPostEliminarAsync, que borra el gasto completo).</summary>
    public async Task<IActionResult> OnPostEliminarComprobanteAsync(long id, CancellationToken ct)
    {
        try
        {
            await _expenses.RemoveReceiptAsync(id, _currentCompany.CompanyId, ct);
            SuccessMessage = "Comprobante eliminado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage("./Detalle", new { id });
    }

    /// <summary>
    /// I5 (revisión final, 2026-10-29): antes elegía ".LastOrDefault()" entre TODAS las
    /// políticas activas del tipo, sin mirar ciudad/perfil -- con varias filas por tipo
    /// (una por ciudad, y opcionalmente una semanal) eso podía mostrar el tope de una
    /// política que no era la que realmente aplicaba a esta línea. Ahora resuelve la MISMA
    /// política real que usa ExpensePolicyService.ValidateAsync (FindApplicablePolicyAsync),
    /// según la ciudad del gasto y el perfil asignado al usuario.
    /// </summary>
    private async Task ResolvePolicyLabelAsync(ExpenseReportLine expense, CancellationToken ct)
    {
        if (expense.ExpenseTypeId is not { } typeId)
        {
            PolicyLabel = null;
            return;
        }

        var userProfileId = await _policyProfiles.GetAssignedProfileIdAsync(_currentCompany.CompanyId, expense.UserId, ct);
        var policy = await _policies.FindApplicablePolicyAsync(_currentCompany.CompanyId, typeId, expense.CityId,
            userProfileId, ExpensePolicyPeriod.PerDocument, ct);
        PolicyLabel = RendicionesUi.PolicyLabel(policy);
    }

    private async Task ResolveWeeklyWarningsAsync(DateTime date, CancellationToken ct)
    {
        WeeklyWarnings = await _weeklyLimits.GetWarningsAsync(_currentCompany.CompanyId, _currentUser.UserId,
            DateOnly.FromDateTime(date), ct);
    }

    private async Task ResolveSuggestedCategoryAsync(ExpenseReportLine expense, CancellationToken ct)
    {
        if (expense.ExpenseTypeId is not null || string.IsNullOrWhiteSpace(expense.SupplierTaxId))
            return;

        var rut = RutChileno.NormalizeOrNull(expense.SupplierTaxId);
        if (rut is null)
            return;

        var hint = await _supplierHints.GetAsync(_currentCompany.CompanyId, rut, ct);
        if (hint?.DefaultExpenseTypeId is not { } catId)
            return;

        var tipo = ExpenseTypes.FirstOrDefault(t => t.Id == catId);
        if (tipo is null)
            return;

        SuggestedCategoryId = tipo.Id;
        SuggestedCategoryName = tipo.Name;
    }

    private async Task<long> SaveReceiptAsync(IFormFile file, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        return await _attachments.SaveAsync(_currentCompany.CompanyId, _currentUser.UserId,
            file.FileName, file.ContentType, stream.ToArray(), ct);
    }

    private async Task ResolvePolicySummaryAsync(CancellationToken ct)
    {
        var group = await _approvalGroups.GetUserGroupAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
        if (group is not null)
            ApprovalGroupSummary = group.Name;

        var costCenters = await _userCostCenters.GetAvailableAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
        var assigned = await _userCostCenters.ListAssignedAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
        if (assigned.Count > 0)
            CostCenterSummary = string.Join(", ", costCenters.Select(c => c.Name));
    }

    private async Task LoadCatalogsAsync(CancellationToken ct)
    {
        ExpenseTypes = await _expenseTypes.ListActiveAsync(_currentCompany.CompanyId, ct);
        DocumentTypes = await _documentTypes.ListActiveAsync(_currentCompany.CompanyId, ct);
        Cities = await _cities.ListActiveAsync(_currentCompany.CompanyId, ct);

        var policies = await _policies.ListAsync(_currentCompany.CompanyId, ct);
        ExpenseTypeIdsWithCityPolicy = policies
            .Where(p => p.IsActive && p.CityId is not null)
            .Select(p => p.ExpenseTypeId)
            .ToHashSet();
    }

    public sealed class ExpenseInput
    {
        [Required(ErrorMessage = "Elegí una categoría.")]
        public long ExpenseTypeId { get; set; }

        public long? DocumentTypeId { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.Today;

        /// <summary>Hora de la transacción -- campo propio, separado de la fecha. Opcional. Relevante para auditoría.</summary>
        [DataType(DataType.Time)]
        public TimeOnly? TransactionTime { get; set; }

        /// <summary>Para un tipo de gasto de kilometraje, se recalcula server-side (ver ExpenseService) -- acá solo se valida &gt; 0 para los tipos normales, ver OnPostGuardarAsync.</summary>
        [Range(0, double.MaxValue)]
        public decimal Amount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? TaxAmount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? NetAmount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? ExemptAmount { get; set; }

        [Required]
        [StringLength(3, MinimumLength = 3, ErrorMessage = "Usar el código de moneda de 3 letras (ej. CLP, USD).")]
        public string Currency { get; set; } = "CLP";

        [StringLength(50)]
        public string? DocumentNumber { get; set; }

        [StringLength(20)]
        public string? SupplierTaxId { get; set; }

        [StringLength(200)]
        public string? SupplierName { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public IFormFile? Receipt { get; set; }

        [StringLength(300)]
        public string? Origin { get; set; }

        [StringLength(300)]
        public string? Destination { get; set; }

        /// <summary>Ciudad donde se incurrió el gasto -- acota la política por ciudad (Task 7). La
        /// UI real (select condicionado a si el tipo tiene una política por ciudad activa) es
        /// Task 10; acá solo se agrega al binding para que el dato llegue a ExpenseService.</summary>
        public long? CityId { get; set; }

        /// <summary>Cantidad (ej. días) cuando ExpenseType.RequiresQuantity -- multiplica el tope
        /// por unidad en la política (Task 7). La UI real (input condicionado a RequiresQuantity)
        /// es Task 10; acá solo se agrega al binding para que el dato llegue a ExpenseService.
        /// [Range] evita un 0 o negativo desde el cliente (I1b, revisión final 2026-10-29) --
        /// ExpensePolicyService.ValidateAsync igual nunca deja pasar una cantidad no positiva a
        /// la multiplicación del tope, esto es la primera barrera en el formulario.</summary>
        [Range(0.01, double.MaxValue, ErrorMessage = "La cantidad debe ser mayor a 0.")]
        public decimal? Quantity { get; set; }
    }
}
