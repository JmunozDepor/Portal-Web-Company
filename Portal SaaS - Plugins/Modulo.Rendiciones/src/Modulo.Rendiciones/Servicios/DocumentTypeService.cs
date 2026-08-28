using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class DocumentTypeService : IDocumentTypeService
{
    private readonly RendicionesDbContext _db;

    public DocumentTypeService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DocumentType>> ListActiveAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.DocumentTypes
            .Where(t => t.CompanyId == companyId && t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DocumentType>> ListAllAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.DocumentTypes
            .Where(t => t.CompanyId == companyId)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, string name, bool appliesTax, decimal taxPercentage, CancellationToken ct = default)
    {
        var type = new DocumentType
        {
            CompanyId = companyId,
            Name = name,
            AppliesTax = appliesTax,
            TaxPercentage = taxPercentage,
            IsActive = true,
        };
        _db.DocumentTypes.Add(type);
        await _db.SaveChangesAsync(ct);
        return type.Id;
    }

    public async Task UpdateAsync(long id, Guid companyId, string name, bool appliesTax, decimal taxPercentage, bool isActive, CancellationToken ct = default)
    {
        var type = await _db.DocumentTypes.FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El tipo de documento no existe o no pertenece a esta compañía.");

        type.Name = name;
        type.AppliesTax = appliesTax;
        type.TaxPercentage = taxPercentage;
        type.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
    }
}
