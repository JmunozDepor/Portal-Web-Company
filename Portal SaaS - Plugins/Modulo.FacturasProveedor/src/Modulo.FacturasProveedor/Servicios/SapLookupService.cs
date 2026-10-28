using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio;
using Modulo.FacturasProveedor.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.FacturasProveedor.Servicios;

/// <summary>Tipos de consulta SAP configurable, con su parámetro y su contrato de columnas.</summary>
public static class SapQueryKinds
{
    public const string PurchaseOrderByDocNum = "purchase_order_by_docnum";
    public const string InvoicedByPurchaseOrder = "invoiced_by_purchase_order";
    public const string SupplierByTaxId = "supplier_by_tax_id";
    public const string SupplierSearch = "supplier_search";

    public static readonly IReadOnlyList<string> All = new[] { PurchaseOrderByDocNum, InvoicedByPurchaseOrder, SupplierByTaxId, SupplierSearch };

    private static readonly string[] SupplierColumns = { "card_code", "card_name", "tax_id", "is_active" };

    public static IReadOnlyList<string> Columns(string kind) => kind switch
    {
        PurchaseOrderByDocNum => new[] { "doc_entry", "doc_num", "series", "card_code", "supplier_tax_id", "doc_date", "total_local", "currency", "status" },
        InvoicedByPurchaseOrder => new[] { "supplier_tax_id", "folio", "total_amount_local", "invoice_doc_entry" },
        SupplierByTaxId or SupplierSearch => SupplierColumns,
        _ => throw new InvalidOperationException($"Tipo de consulta SAP desconocido: «{kind}»."),
    };

    public static string Parameter(string kind) => kind switch
    {
        PurchaseOrderByDocNum => "doc_num",
        InvoicedByPurchaseOrder => "doc_entry",
        SupplierByTaxId => "tax_id",
        SupplierSearch => "text",
        _ => throw new InvalidOperationException($"Tipo de consulta SAP desconocido: «{kind}»."),
    };

    /// <summary>Parámetro obligatorio y permitidos, en el formato de SapSqlGuard.Check.</summary>
    public static (string? Required, string[] Allowed) Parameters(string kind) => (Parameter(kind), new[] { Parameter(kind) });

    public static string Label(string kind) => kind switch
    {
        PurchaseOrderByDocNum => "Orden de compra por DocNum",
        InvoicedByPurchaseOrder => "Facturado por orden de compra (opcional)",
        SupplierByTaxId => "Proveedor por RUT",
        SupplierSearch => "Búsqueda de proveedores",
        _ => kind,
    };

    internal static string SeedFile(string kind) => kind switch
    {
        PurchaseOrderByDocNum => "hana_purchase_order_by_docnum.sql",
        InvoicedByPurchaseOrder => "hana_invoiced_by_purchase_order.sql",
        SupplierByTaxId => "hana_supplier_by_tax_id.sql",
        SupplierSearch => "hana_supplier_search.sql",
        _ => throw new InvalidOperationException($"Tipo de consulta SAP desconocido: «{kind}»."),
    };
}

public sealed record SapPurchaseOrder(
    int DocEntry, int DocNum, string Series, string CardCode, string? SupplierTaxId, DateOnly DocDate, decimal TotalLocal, string Currency, string Status);

public sealed record SapSupplier(string CardCode, string CardName, string TaxId, bool IsActive);

public sealed record QueryValidationResult(bool IsValid, IReadOnlyList<string> MissingColumns, string? Error);

public sealed record SapQueryDto(
    string Kind, string Label, string Parameter, IReadOnlyList<string> Columns, string SqlText, bool IsActive, DateTimeOffset? ValidatedAt);

public interface ISapLookupService
{
    Task<IReadOnlyList<SapPurchaseOrder>> FindPurchaseOrdersAsync(int docNum, CancellationToken ct);
    Task<IReadOnlyList<InvoicedAgainstOc>> FindInvoicedAsync(int docEntry, CancellationToken ct);
    Task<SapSupplier?> FindSupplierByTaxIdAsync(string taxId, CancellationToken ct);
    Task<IReadOnlyList<SapSupplier>> SearchSuppliersAsync(string text, CancellationToken ct);
    Task<IReadOnlyList<SapQueryDto>> ListQueriesAsync(CancellationToken ct);
    Task SaveQueryAsync(string kind, string sql, bool isActive, CancellationToken ct);
    Task<QueryValidationResult> TestQueryAsync(string kind, string sql, string sampleValue, CancellationToken ct);
}

/// <summary>
/// Consultas SAP de solo lectura, configurables por compañía (mismo patrón que sap_document_queries de Despachos):
/// el SQL vive en fp_sap_queries, con seeds HANA cargados la primera vez, y se valida antes de guardar o probar. Así
/// una compañía con otra base SAP u otros campos (ej. Block) se adapta sin programar.
/// </summary>
public sealed class SapLookupService : ISapLookupService
{
    private readonly FacturasProveedorDbContext _db;
    private readonly IHanaService _hana;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly TimeProvider _clock;

    public SapLookupService(FacturasProveedorDbContext db, IHanaService hana, ICurrentCompanyAccessor company,
        ICurrentUserContext user, TimeProvider clock)
    {
        _db = db;
        _hana = hana;
        _company = company;
        _user = user;
        _clock = clock;
    }

    private Guid CompanyId => _company.CompanyId;

    public async Task<IReadOnlyList<SapPurchaseOrder>> FindPurchaseOrdersAsync(int docNum, CancellationToken ct)
    {
        var query = await RequireActiveAsync(SapQueryKinds.PurchaseOrderByDocNum, ct);
        var rows = await _hana.QueryDynamicAsync(SapSqlGuard.Executable(query.SqlText), new { doc_num = docNum }, ct);
        return rows.Select(Ci).Select(r => new SapPurchaseOrder(
            Int(r, "doc_entry"), Int(r, "doc_num"), Str(r, "series") ?? "", Str(r, "card_code") ?? "",
            NormalizeRut(Str(r, "supplier_tax_id")), Date(r, "doc_date"), Dec(r, "total_local"), Str(r, "currency") ?? "",
            (Str(r, "status") ?? "open").ToLowerInvariant())).ToList();
    }

    public async Task<IReadOnlyList<InvoicedAgainstOc>> FindInvoicedAsync(int docEntry, CancellationToken ct)
    {
        var query = await GetQueryAsync(SapQueryKinds.InvoicedByPurchaseOrder, ct);
        if (query is not { IsActive: true }) return Array.Empty<InvoicedAgainstOc>();
        var rows = await _hana.QueryDynamicAsync(SapSqlGuard.Executable(query.SqlText), new { doc_entry = docEntry }, ct);
        return rows.Select(Ci).Select(r => new InvoicedAgainstOc(
            NormalizeRut(Str(r, "supplier_tax_id")) ?? "", (long)Dec(r, "folio"), Dec(r, "total_amount_local"))).ToList();
    }

    public async Task<SapSupplier?> FindSupplierByTaxIdAsync(string taxId, CancellationToken ct)
    {
        var rut = NormalizeRut(taxId) ?? throw new InvalidOperationException("Ingrese un RUT de proveedor.");
        var query = await RequireActiveAsync(SapQueryKinds.SupplierByTaxId, ct);
        var rows = await _hana.QueryDynamicAsync(SapSqlGuard.Executable(query.SqlText), new { tax_id = rut }, ct);
        return rows.Select(Ci).Select(ToSupplier).FirstOrDefault();
    }

    public async Task<IReadOnlyList<SapSupplier>> SearchSuppliersAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 2) return Array.Empty<SapSupplier>();
        var query = await RequireActiveAsync(SapQueryKinds.SupplierSearch, ct);
        var rows = await _hana.QueryDynamicAsync(SapSqlGuard.Executable(query.SqlText), new { text = text.Trim() }, ct);
        return rows.Select(Ci).Select(ToSupplier).Take(20).ToList();
    }

    public async Task<IReadOnlyList<SapQueryDto>> ListQueriesAsync(CancellationToken ct)
    {
        await EnsureSeedsAsync(ct);
        var rows = await _db.SapQueries.AsNoTracking().Where(q => q.CompanyId == CompanyId).ToListAsync(ct);
        return SapQueryKinds.All.Select(kind => rows.Single(r => r.QueryKind == kind)).Select(q => new SapQueryDto(
            q.QueryKind, SapQueryKinds.Label(q.QueryKind), SapQueryKinds.Parameter(q.QueryKind), SapQueryKinds.Columns(q.QueryKind),
            q.SqlText, q.IsActive, q.ValidatedAt)).ToList();
    }

    public async Task SaveQueryAsync(string kind, string sql, bool isActive, CancellationToken ct)
    {
        if (SapSqlGuard.Check(sql, SapQueryKinds.Parameters(kind)) is { } error) throw new InvalidOperationException(error);
        await EnsureSeedsAsync(ct);
        var row = await _db.SapQueries.SingleAsync(q => q.CompanyId == CompanyId && q.QueryKind == kind, ct);
        var before = JsonSerializer.Serialize(new { row.SqlText, row.IsActive });
        row.SqlText = sql.Trim();
        row.IsActive = isActive;
        row.UpdatedAt = _clock.GetUtcNow();
        row.UpdatedBy = _user.UserId;
        _db.ConfigEvents.Add(new FpConfigEvent
        {
            CompanyId = CompanyId, EntityType = "sap_query", EntityKey = kind, BeforeJson = before,
            AfterJson = JsonSerializer.Serialize(new { row.SqlText, row.IsActive }), UserId = _user.UserId, OccurredAt = _clock.GetUtcNow(),
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<QueryValidationResult> TestQueryAsync(string kind, string sql, string sampleValue, CancellationToken ct)
    {
        if (SapSqlGuard.Check(sql, SapQueryKinds.Parameters(kind)) is { } error)
            return new QueryValidationResult(false, Array.Empty<string>(), error);

        object parameters = kind switch
        {
            SapQueryKinds.PurchaseOrderByDocNum => new { doc_num = ParseInt(sampleValue) },
            SapQueryKinds.InvoicedByPurchaseOrder => new { doc_entry = ParseInt(sampleValue) },
            SapQueryKinds.SupplierByTaxId => new { tax_id = NormalizeRut(sampleValue) ?? "" },
            _ => new { text = sampleValue.Trim() },
        };
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            rows = await _hana.QueryDynamicAsync(SapSqlGuard.Executable(sql), parameters, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new QueryValidationResult(false, Array.Empty<string>(), $"SAP rechazó la consulta: {ex.Message}");
        }

        if (rows.Count == 0)
            return new QueryValidationResult(true, Array.Empty<string>(),
                "La consulta no devolvió filas para el valor de prueba: no se pudieron verificar las columnas.");
        var keys = rows[0].Keys.Select(k => k.ToLowerInvariant()).ToHashSet();
        var missing = SapQueryKinds.Columns(kind).Where(c => !keys.Contains(c)).ToList();
        if (missing.Count > 0) return new QueryValidationResult(false, missing, $"Faltan columnas: {string.Join(", ", missing)}.");

        var stored = await GetQueryAsync(kind, ct);
        if (stored is not null && stored.SqlText == sql.Trim())
        {
            stored.ValidatedAt = _clock.GetUtcNow();
            await _db.SaveChangesAsync(ct);
        }
        return new QueryValidationResult(true, Array.Empty<string>(), null);
    }

    private async Task<FpSapQuery?> GetQueryAsync(string kind, CancellationToken ct)
    {
        await EnsureSeedsAsync(ct);
        return await _db.SapQueries.FirstOrDefaultAsync(q => q.CompanyId == CompanyId && q.QueryKind == kind, ct);
    }

    private async Task<FpSapQuery> RequireActiveAsync(string kind, CancellationToken ct)
    {
        var query = await GetQueryAsync(kind, ct);
        return query is { IsActive: true }
            ? query
            : throw new InvalidOperationException($"La consulta SAP «{SapQueryKinds.Label(kind)}» está desactivada: revísela en Configuración › Consultas SAP.");
    }

    private async Task EnsureSeedsAsync(CancellationToken ct)
    {
        var existing = await _db.SapQueries.Where(q => q.CompanyId == CompanyId).Select(q => q.QueryKind).ToListAsync(ct);
        var missing = SapQueryKinds.All.Except(existing).ToList();
        if (missing.Count == 0) return;
        foreach (var kind in missing)
            _db.SapQueries.Add(new FpSapQuery { CompanyId = CompanyId, QueryKind = kind, SqlText = Seed(kind), IsActive = true });
        await _db.SaveChangesAsync(ct);
    }

    internal static string Seed(string kind)
    {
        var assembly = typeof(SapLookupService).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(SapQueryKinds.SeedFile(kind), StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    private static SapSupplier ToSupplier(IReadOnlyDictionary<string, object?> r) =>
        new(Str(r, "card_code") ?? "", Str(r, "card_name") ?? "", NormalizeRut(Str(r, "tax_id")) ?? "", Dec(r, "is_active") != 0);

    private static IReadOnlyDictionary<string, object?> Ci(IReadOnlyDictionary<string, object?> row) =>
        new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);

    private static string? Str(IReadOnlyDictionary<string, object?> r, string key) =>
        r.TryGetValue(key, out var v) && v is not null and not DBNull ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() : null;

    private static decimal Dec(IReadOnlyDictionary<string, object?> r, string key) =>
        r.TryGetValue(key, out var v) && v is not null and not DBNull ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m;

    private static int Int(IReadOnlyDictionary<string, object?> r, string key) => (int)Dec(r, key);

    private static DateOnly Date(IReadOnlyDictionary<string, object?> r, string key) =>
        r.TryGetValue(key, out var v) switch
        {
            true when v is DateTime dt => DateOnly.FromDateTime(dt),
            true when v is DateOnly d => d,
            true when v is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var p) => DateOnly.FromDateTime(p),
            _ => DateOnly.MinValue,
        };

    private static int ParseInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n : throw new InvalidOperationException("El valor de prueba debe ser un número.");

    /// <summary>"76.412.553-k" → "76412553-K"; null si viene vacío.</summary>
    internal static string? NormalizeRut(string? rut)
    {
        if (string.IsNullOrWhiteSpace(rut)) return null;
        try
        {
            return RutChileno.Normalize(rut);
        }
        catch (ArgumentException)
        {
            return rut.Trim().ToUpperInvariant();
        }
    }
}
