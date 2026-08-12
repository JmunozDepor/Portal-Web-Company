using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class AttachmentStorageService : IAttachmentStorageService
{
    private readonly RendicionesDbContext _db;

    public AttachmentStorageService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<long> SaveAsync(Guid companyId, Guid userId, string fileName, string mimeType, byte[] content, CancellationToken ct = default)
    {
        var receipt = new ExpenseReceipt
        {
            CompanyId = companyId,
            UserId = userId,
            FileName = fileName,
            MimeType = mimeType,
            SizeBytes = content.Length,
            Content = content,
        };
        _db.ExpenseReceipts.Add(receipt);
        await _db.SaveChangesAsync(ct);
        return receipt.Id;
    }

    public async Task<ExpenseReceipt?> GetAsync(long id, Guid companyId, CancellationToken ct = default) =>
        await _db.ExpenseReceipts.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct);

    public async Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default)
    {
        var receipt = await _db.ExpenseReceipts.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct);
        if (receipt is null)
            return;

        _db.ExpenseReceipts.Remove(receipt);
        await _db.SaveChangesAsync(ct);
    }
}
