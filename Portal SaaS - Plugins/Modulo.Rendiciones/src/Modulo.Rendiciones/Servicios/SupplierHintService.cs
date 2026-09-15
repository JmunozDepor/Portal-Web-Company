using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class SupplierHintService : ISupplierHintService
{
    private readonly RendicionesDbContext _db;

    public SupplierHintService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<SupplierHint?> GetAsync(Guid companyId, string normalizedRut, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedRut))
            return null;

        return await _db.SupplierHints
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.CompanyId == companyId && h.SupplierTaxId == normalizedRut, ct);
    }

    public async Task RegisterAsync(Guid companyId, string? rawRut, string? supplierName,
        long? expenseTypeId, long? documentTypeId, CancellationToken ct = default)
    {
        var rut = RutChileno.NormalizeOrNull(rawRut);
        if (rut is null)
            return; // sin RUT válido no hay nada que aprender

        var hint = await _db.SupplierHints
            .FirstOrDefaultAsync(h => h.CompanyId == companyId && h.SupplierTaxId == rut, ct);

        if (hint is null)
        {
            hint = new SupplierHint { CompanyId = companyId, SupplierTaxId = rut };
            _db.SupplierHints.Add(hint);
        }

        if (!string.IsNullOrWhiteSpace(supplierName))
            hint.SupplierName = supplierName.Trim();

        hint.TimesSeen++;
        hint.LastSeenAt = DateTimeOffset.UtcNow;

        if (documentTypeId is { } docId)
            hint.DefaultDocumentTypeId = docId; // el tipo de documento es estable por proveedor: última gana

        if (expenseTypeId is { } catId)
        {
            var counts = new Dictionary<long, int>(hint.CategoryCounts);
            counts[catId] = counts.GetValueOrDefault(catId) + 1;
            hint.CategoryCounts = counts;

            // Moda: la categoría con más confirmaciones (desempate: la recién sumada).
            var mejor = counts.Aggregate((a, b) =>
                b.Value > a.Value || (b.Value == a.Value && b.Key == catId) ? b : a);
            hint.DefaultExpenseTypeId = mejor.Key;
            hint.TimesCategoryConfirmed = mejor.Value;
        }

        await _db.SaveChangesAsync(ct);
    }
}
