using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Única implementación de ICatalogSyncProvider hoy -- envuelve los catálogos SAP en
/// vivo y hace upsert en las tablas locales rendiciones_cost_centers/rendiciones_gl_accounts.
/// Filas ausentes en el resultado de SAP se desactivan (IsActive = false), nunca se borran,
/// porque pueden estar referenciadas históricamente por gastos ya guardados.
/// </summary>
public sealed class SapCatalogSyncProvider : ICatalogSyncProvider
{
    private readonly RendicionesDbContext _db;
    private readonly ICostCenterCatalogService _costCenters;
    private readonly IGeneralLedgerAccountCatalogService _glAccounts;

    public SapCatalogSyncProvider(RendicionesDbContext db, ICostCenterCatalogService costCenters, IGeneralLedgerAccountCatalogService glAccounts)
    {
        _db = db;
        _costCenters = costCenters;
        _glAccounts = glAccounts;
    }

    public async Task<CatalogSyncResult> SyncCostCentersAsync(Guid companyId, CancellationToken ct = default)
    {
        var sapItems = await _costCenters.ListAsync(ct: ct);
        var existing = await _db.CostCenters.Where(c => c.CompanyId == companyId).ToListAsync(ct);
        return await UpsertAsync(existing, sapItems.Select(i => (i.Code, i.Name)).ToList(), companyId,
            (companyIdArg, code, name) => new CostCenter { CompanyId = companyIdArg, Code = code, Name = name, Source = CatalogEntrySource.Sap }, ct);
    }

    public async Task<CatalogSyncResult> SyncGlAccountsAsync(Guid companyId, CancellationToken ct = default)
    {
        var sapItems = await _glAccounts.ListAsync(ct: ct);
        var existing = await _db.GlAccounts.Where(g => g.CompanyId == companyId).ToListAsync(ct);
        return await UpsertAsync(existing, sapItems.Select(i => (i.AccountCode, i.AccountName)).ToList(), companyId,
            (companyIdArg, code, name) => new GlAccount { CompanyId = companyIdArg, Code = code, Name = name, Source = CatalogEntrySource.Sap }, ct);
    }

    private async Task<CatalogSyncResult> UpsertAsync<TEntity>(
        List<TEntity> existing,
        IReadOnlyList<(string Code, string Name)> sapItems,
        Guid companyId,
        Func<Guid, string, string, TEntity> create,
        CancellationToken ct)
        where TEntity : class
    {
        int created = 0, updated = 0, deactivated = 0;
        var warnings = new List<string>();
        var seenCodes = new HashSet<string>();
        var validItems = new List<(string Code, string Name)>();

        foreach (var (rawCode, rawName) in sapItems)
        {
            var code = rawCode?.Trim() ?? "";
            var name = rawName?.Trim() ?? "";

            if (code.Length == 0)
            {
                warnings.Add($"Ítem de SAP omitido: código vacío (nombre: \"{name}\").");
                continue;
            }
            if (name.Length == 0)
            {
                warnings.Add($"Ítem de SAP omitido: nombre vacío (código: \"{code}\").");
                continue;
            }
            if (!seenCodes.Add(code))
            {
                warnings.Add($"Ítem de SAP omitido: código duplicado \"{code}\" en la respuesta.");
                continue;
            }

            validItems.Add((code, name));
        }

        var sapCodes = new HashSet<string>(validItems.Select(i => i.Code));

        foreach (var (code, name) in validItems)
        {
            var row = existing.FirstOrDefault(e => GetCode(e) == code);
            if (row is null)
            {
                _db.Set<TEntity>().Add(create(companyId, code, name));
                created++;
            }
            else if (GetName(row) != name || !GetIsActive(row) || GetSource(row) != CatalogEntrySource.Sap)
            {
                SetName(row, name);
                SetIsActive(row, true);
                SetSource(row, CatalogEntrySource.Sap);
                SetUpdatedAt(row);
                updated++;
            }
        }

        foreach (var row in existing.Where(e => GetIsActive(e) && !sapCodes.Contains(GetCode(e))))
        {
            SetIsActive(row, false);
            SetUpdatedAt(row);
            deactivated++;
        }

        await _db.SaveChangesAsync(ct);
        return new CatalogSyncResult(created, updated, deactivated, warnings);
    }

    private static string GetCode(object e) => e switch { CostCenter c => c.Code, GlAccount g => g.Code, _ => throw new NotSupportedException() };
    private static string GetName(object e) => e switch { CostCenter c => c.Name, GlAccount g => g.Name, _ => throw new NotSupportedException() };
    private static bool GetIsActive(object e) => e switch { CostCenter c => c.IsActive, GlAccount g => g.IsActive, _ => throw new NotSupportedException() };
    private static CatalogEntrySource GetSource(object e) => e switch { CostCenter c => c.Source, GlAccount g => g.Source, _ => throw new NotSupportedException() };
    private static void SetName(object e, string name) { if (e is CostCenter c) c.Name = name; else if (e is GlAccount g) g.Name = name; }
    private static void SetIsActive(object e, bool value) { if (e is CostCenter c) c.IsActive = value; else if (e is GlAccount g) g.IsActive = value; }
    private static void SetSource(object e, CatalogEntrySource source) { if (e is CostCenter c) c.Source = source; else if (e is GlAccount g) g.Source = source; }
    private static void SetUpdatedAt(object e) { if (e is CostCenter c) c.UpdatedAt = DateTimeOffset.UtcNow; else if (e is GlAccount g) g.UpdatedAt = DateTimeOffset.UtcNow; }
}
