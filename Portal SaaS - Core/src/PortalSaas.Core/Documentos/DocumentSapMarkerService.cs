using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Compras;
using PortalSaas.Core.Inventario;
using PortalSaas.Core.Ventas;
using PortalSaas.Data;

namespace PortalSaas.Core.Documentos;

/// <summary>Ver IDocumentSapMarkerService. Compañía = la activa (sesión o ICurrentCompanyOverride en segundo plano).</summary>
public sealed class DocumentSapMarkerService : IDocumentSapMarkerService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly IHanaService _hana;
    private readonly ILogger<DocumentSapMarkerService> _logger;

    public DocumentSapMarkerService(PortalSaasDbContext db, ICurrentCompanyAccessor company, IHanaService hana, ILogger<DocumentSapMarkerService> logger)
    {
        _db = db;
        _company = company;
        _hana = hana;
        _logger = logger;
    }

    /// <summary>"(BR-" + 12 hex del SHA-256 de la clave + ")" -- corto, determinístico, sin corchetes (en LIKE de SQL Server son comodín).</summary>
    internal static string CommentsTagFor(string key) =>
        $"(BR-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12].ToLowerInvariant()})";

    public async Task<DocumentSapMarker> GetMarkerAsync(string key, CancellationToken ct = default) =>
        new(await GetUdfNameAsync(ct), key, CommentsTagFor(key));

    public async Task<int?> FindDocEntryAsync(string engine, string documentType, string key, CancellationToken ct = default)
    {
        var table = ResolveTable(engine, documentType);
        var udf = await GetUdfNameAsync(ct);
        var parameters = new Dictionary<string, object?> { ["tag"] = $"%{CommentsTagFor(key)}%", ["marker"] = key };

        if (udf is not null)
        {
            try
            {
                var byUdf = await _hana.QueryAsync<int>(
                    $"""SELECT "DocEntry" FROM "{table}" WHERE "CANCELED" = 'N' AND ("{udf}" = :marker OR "Comments" LIKE :tag) ORDER BY "DocEntry" DESC""",
                    parameters, ct);
                return byUdf.Count > 0 ? byUdf[0] : null;
            }
            catch (Exception ex)
            {
                // UDF configurado pero inexistente en esa tabla de SAP: igual se busca por
                // Comentarios (nunca dejar de verificar por un error de configuración).
                _logger.LogWarning(ex, "No se pudo buscar la marca en el UDF {Udf} de {Table}; se busca solo en Comentarios.", udf, table);
            }
        }

        var byComments = await _hana.QueryAsync<int>(
            $"""SELECT "DocEntry" FROM "{table}" WHERE "CANCELED" = 'N' AND "Comments" LIKE :tag ORDER BY "DocEntry" DESC""",
            parameters, ct);
        return byComments.Count > 0 ? byComments[0] : null;
    }

    private async Task<string?> GetUdfNameAsync(CancellationToken ct)
    {
        if (!_company.HasCompany)
        {
            return null;
        }

        var companyId = _company.CompanyId;
        var udf = await _db.CompanyDocumentSettings.AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => s.DraftMarkerUdfName)
            .FirstOrDefaultAsync(ct);

        // Defensa en profundidad: el nombre se interpola en SQL.
        return udf is not null && CompanyDocumentSettingsService.UdfNamePattern().IsMatch(udf) ? udf : null;
    }

    private static string ResolveTable(string engine, string documentType) => engine switch
    {
        DocumentDraftEngine.Sales => SalesDocumentTypeCatalog.Resolve(Enum.Parse<SalesDocumentType>(documentType)).Table,
        DocumentDraftEngine.Purchase => PurchaseDocumentTypeCatalog.Resolve(Enum.Parse<PurchaseDocumentType>(documentType)).Table,
        DocumentDraftEngine.Inventory => InventoryDocumentTypeCatalog.Resolve(Enum.Parse<InventoryDocumentType>(documentType)).Table,
        _ => throw new InvalidOperationException($"Motor de documento desconocido: {engine}."),
    };
}
