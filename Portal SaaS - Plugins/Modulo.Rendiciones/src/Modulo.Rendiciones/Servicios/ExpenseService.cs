using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExpenseService : IExpenseService
{
    private readonly RendicionesDbContext _db;
    private readonly IAttachmentStorageService _attachments;
    private readonly IExpensePolicyService _policies;
    private readonly IRoutingService _routing;

    public ExpenseService(RendicionesDbContext db, IAttachmentStorageService attachments, IExpensePolicyService policies, IRoutingService routing)
    {
        _db = db;
        _attachments = attachments;
        _policies = policies;
        _routing = routing;
    }

    public async Task<IReadOnlyList<ExpenseReportLine>> ListLooseAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
        await _db.ExpenseReportLines
            .Include(d => d.ExpenseType)
            .Include(d => d.DocumentType)
            .Where(d => d.CompanyId == companyId && d.UserId == userId && d.Status == "Loose")
            .OrderByDescending(d => d.Date)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExpenseReportLine>> ListAllAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
        await _db.ExpenseReportLines
            .Include(d => d.ExpenseType)
            .Include(d => d.DocumentType)
            .Include(d => d.ExpenseReport)
            .Where(d => d.CompanyId == companyId && d.UserId == userId)
            .OrderByDescending(d => d.Date)
            .ToListAsync(ct);

    public async Task<ExpenseReportLine?> GetAsync(long id, Guid companyId, CancellationToken ct = default) =>
        await _db.ExpenseReportLines
            .Include(d => d.ExpenseType)
            .Include(d => d.DocumentType)
            .Include(d => d.ExpenseReceipt)
            .Include(d => d.ExpenseReport)
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId, ct);

    public async Task<ExpenseSavedResult> CreateAsync(ExpenseReportLine expense, CancellationToken ct = default)
    {
        await ApplyMileageIfApplicableAsync(expense, ct);

        var result = await _policies.ValidateAsync(expense.CompanyId, expense.UserId, expense.ExpenseTypeId, expense.Amount,
            expense.DocumentNumber, expense.SupplierTaxId, expenseIdToExclude: null, ct);

        if (result.Blocked)
            throw new InvalidOperationException(string.Join(" ", result.Warnings));

        expense.Status = "Loose";
        expense.ExpenseReportId = null;
        expense.Date = NormalizeDate(expense.Date);
        _db.ExpenseReportLines.Add(expense);
        await _db.SaveChangesAsync(ct);
        return new ExpenseSavedResult(expense.Id, result.Warnings);
    }

    public async Task<IReadOnlyList<string>> UpdateAsync(long id, Guid companyId, ExpenseReportLine data, CancellationToken ct = default)
    {
        var expense = await RequireLooseAsync(id, companyId, ct);

        await ApplyMileageIfApplicableAsync(data, ct);

        var result = await _policies.ValidateAsync(companyId, expense.UserId, data.ExpenseTypeId, data.Amount,
            data.DocumentNumber, data.SupplierTaxId, expenseIdToExclude: id, ct);

        if (result.Blocked)
            throw new InvalidOperationException(string.Join(" ", result.Warnings));

        expense.ExpenseTypeId = data.ExpenseTypeId;
        expense.DocumentTypeId = data.DocumentTypeId;
        expense.Date = NormalizeDate(data.Date);
        expense.Amount = data.Amount;
        expense.TaxAmount = data.TaxAmount;
        expense.Currency = data.Currency;
        expense.DocumentNumber = data.DocumentNumber;
        expense.SupplierTaxId = data.SupplierTaxId;
        expense.SupplierName = data.SupplierName;
        expense.Notes = data.Notes;
        expense.Origin = data.Origin;
        expense.Destination = data.Destination;
        expense.DistanceKm = data.DistanceKm;
        expense.AppliedRatePerKm = data.AppliedRatePerKm;
        if (data.ExpenseReceiptId is not null)
            expense.ExpenseReceiptId = data.ExpenseReceiptId;

        await _db.SaveChangesAsync(ct);
        return result.Warnings;
    }

    public async Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default)
    {
        var expense = await RequireLooseAsync(id, companyId, ct);
        var receiptId = expense.ExpenseReceiptId;

        _db.ExpenseReportLines.Remove(expense);
        await _db.SaveChangesAsync(ct);

        if (receiptId is { } id2)
            await _attachments.DeleteAsync(id2, companyId, ct);
    }

    /// <summary>
    /// Si ExpenseType.IsMileage, recalcula Amount/DistanceKm/AppliedRatePerKm
    /// server-side vía IRoutingService -- nunca confía en lo que el cliente haya
    /// mandado en Amount/DistanceKm (esos campos quedan ocultos/deshabilitados en el
    /// formulario para este caso, pero igual se ignoran acá por si alguien arma el
    /// POST a mano). Si no es kilometraje, no toca nada.
    /// </summary>
    private async Task ApplyMileageIfApplicableAsync(ExpenseReportLine data, CancellationToken ct)
    {
        if (data.ExpenseTypeId is not { } expenseTypeId)
            return;

        var expenseType = await _db.ExpenseTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == expenseTypeId, ct);
        if (expenseType is not { IsMileage: true })
            return;

        if (string.IsNullOrWhiteSpace(data.Origin) || string.IsNullOrWhiteSpace(data.Destination))
            throw new InvalidOperationException("Este tipo de gasto requiere origen y destino para calcular el kilometraje.");

        if (expenseType.RatePerKm is not { } rate || rate <= 0)
            throw new InvalidOperationException($"El tipo de gasto '{expenseType.Name}' no tiene una tarifa por km configurada -- definila en Administración antes de usarlo.");

        var route = await _routing.CalculateDistanceAsync(data.CompanyId, data.Origin!, data.Destination!, ct);
        if (route.Error is not null || route.DistanceKm is not { } km)
            throw new InvalidOperationException($"No se pudo calcular la ruta: {route.Error ?? "respuesta vacía del proveedor de mapas."}");

        data.DistanceKm = km;
        data.AppliedRatePerKm = rate;
        data.Amount = Math.Round(km * rate, 0);
        data.TaxAmount = null;
        // TODO: moneda del kilometraje fija por ahora -- generalizar a la moneda local
        // de la organización cuando este plugin soporte más de un país (ver
        // PENDIENTE.md, mismo criterio que Country en Organization/Company).
        data.Currency = "CLP";
    }

    public async Task RemoveReceiptAsync(long id, Guid companyId, CancellationToken ct = default)
    {
        var expense = await RequireLooseAsync(id, companyId, ct);
        var receiptId = expense.ExpenseReceiptId;
        if (receiptId is null)
            return;

        expense.ExpenseReceiptId = null;
        await _db.SaveChangesAsync(ct);
        await _attachments.DeleteAsync(receiptId.Value, companyId, ct);
    }

    private async Task<ExpenseReportLine> RequireLooseAsync(long id, Guid companyId, CancellationToken ct)
    {
        var expense = await _db.ExpenseReportLines.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El gasto no existe o no pertenece a esta compañía.");

        if (expense.Status != "Loose")
            throw new InvalidOperationException("El gasto ya pertenece a un informe -- desvincúlalo primero para editarlo o eliminarlo.");

        return expense;
    }

    /// <summary>
    /// La fecha del gasto es un dato de calendario (sin hora ni zona) pero la columna es
    /// <c>timestamptz</c>: Npgsql 8 rechaza un DateTimeOffset con offset != 0. Los orígenes
    /// (OCR con <c>DateTime.Today</c>, <c>&lt;input type="date"&gt;</c> ligado a DateTime)
    /// llegan con offset local (-03:00 en Chile) y revientan el SaveChanges. Se guarda el
    /// día elegido tal cual, a medianoche UTC.
    /// </summary>
    private static DateTimeOffset NormalizeDate(DateTimeOffset value) =>
        new(value.Date, TimeSpan.Zero);
}
