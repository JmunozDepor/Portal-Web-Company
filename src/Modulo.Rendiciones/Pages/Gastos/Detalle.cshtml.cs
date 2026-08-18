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
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public DetalleModel(IExpenseService expenses, IExpenseTypeService expenseTypes, IDocumentTypeService documentTypes,
        IExpensePolicyService policies, IExpenseApprovalGroupService approvalGroups, IUserCostCenterService userCostCenters,
        IAttachmentStorageService attachments, IRoutingService routing, IRendicionesUserRoleService roles,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
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
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public bool IsNew { get; private set; } = true;
    public ExpenseReportLine? Expense { get; private set; }

    /// <summary>"Sin política" / "Tope N" / "Sin tope" -- mismo cálculo que Pages/Gastos/Index, ver
    /// el comentario de PolicyByExpenseType ahí. Se resuelve una sola vez para el gasto actual,
    /// no hace falta un diccionario acá (a diferencia del listado, es un solo gasto por página).</summary>
    public string? PolicyLabel { get; private set; }

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

    [BindProperty]
    public ExpenseInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(long? id, CancellationToken ct)
    {
        await LoadCatalogsAsync(ct);
        await ResolvePolicySummaryAsync(ct);

        if (id is null)
            return Page();

        var expense = await _expenses.GetAsync(id.Value, _currentCompany.CompanyId, ct);
        if (expense is null || expense.UserId != _currentUser.UserId)
            return NotFound();

        IsNew = false;
        Expense = expense;
        await ResolvePolicyLabelAsync(expense, ct);
        Input = new ExpenseInput
        {
            ExpenseTypeId = expense.ExpenseTypeId ?? 0,
            DocumentTypeId = expense.DocumentTypeId,
            Date = expense.Date.DateTime,
            Amount = expense.Amount,
            TaxAmount = expense.TaxAmount,
            Currency = expense.Currency,
            DocumentNumber = expense.DocumentNumber,
            SupplierTaxId = expense.SupplierTaxId,
            SupplierName = expense.SupplierName,
            Notes = expense.Notes,
            Origin = expense.Origin,
            Destination = expense.Destination,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long? id, CancellationToken ct)
    {
        await LoadCatalogsAsync(ct);
        await ResolvePolicySummaryAsync(ct);
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
                Amount = Input.Amount,
                TaxAmount = Input.TaxAmount,
                Currency = Input.Currency,
                DocumentNumber = Input.DocumentNumber,
                SupplierTaxId = Input.SupplierTaxId,
                SupplierName = Input.SupplierName,
                Notes = Input.Notes,
                Origin = isMileage ? Input.Origin : null,
                Destination = isMileage ? Input.Destination : null,
                ExpenseReceiptId = receiptId,
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

    private async Task ResolvePolicyLabelAsync(ExpenseReportLine expense, CancellationToken ct)
    {
        if (expense.ExpenseTypeId is not { } typeId)
        {
            PolicyLabel = null;
            return;
        }

        var policies = await _policies.ListAsync(_currentCompany.CompanyId, ct);
        var policy = policies.Where(p => p.IsActive && p.ExpenseTypeId == typeId).LastOrDefault();
        PolicyLabel = policy is null ? "Sin política" : (policy.MaxAmount is { } max ? $"Tope {max:N0}" : "Sin tope");
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
    }

    public sealed class ExpenseInput
    {
        [Required(ErrorMessage = "Elegí una categoría.")]
        public long ExpenseTypeId { get; set; }

        public long? DocumentTypeId { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.Today;

        /// <summary>Para un tipo de gasto de kilometraje, se recalcula server-side (ver ExpenseService) -- acá solo se valida &gt; 0 para los tipos normales, ver OnPostGuardarAsync.</summary>
        [Range(0, double.MaxValue)]
        public decimal Amount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? TaxAmount { get; set; }

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
    }
}
