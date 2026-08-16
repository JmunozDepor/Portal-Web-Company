namespace Modulo.Rendiciones.Servicios;

public interface ICatalogSyncProvider
{
    Task<CatalogSyncResult> SyncCostCentersAsync(Guid companyId, CancellationToken ct = default);
    Task<CatalogSyncResult> SyncGlAccountsAsync(Guid companyId, CancellationToken ct = default);
}

public sealed record CatalogSyncResult(int Created, int Updated, int Deactivated, IReadOnlyList<string> Warnings);
