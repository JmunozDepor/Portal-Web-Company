using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class CityService : ICityService
{
    private readonly RendicionesDbContext _db;

    public CityService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<City>> ListActiveAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.Cities
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<City>> ListAllAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.Cities
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, string name, CancellationToken ct = default)
    {
        var exists = await _db.Cities
            .AnyAsync(c => c.CompanyId == companyId && c.Name == name, ct);
        if (exists)
            throw new InvalidOperationException("Ya existe una ciudad con ese nombre en esta compañía.");

        var city = new City
        {
            CompanyId = companyId,
            Name = name,
            IsActive = true,
        };
        _db.Cities.Add(city);
        await _db.SaveChangesAsync(ct);
        return city.Id;
    }

    public async Task UpdateAsync(long id, Guid companyId, string name, bool isActive, CancellationToken ct = default)
    {
        var city = await _db.Cities.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La ciudad no existe o no pertenece a esta compañía.");

        city.Name = name;
        city.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
    }
}
