using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

/// <summary>
/// Búsqueda por folio con consultas SAP configurables por compañía. El SQL vive en
/// sap_document_queries (editable); los seeds de HANA se cargan perezosamente la primera
/// vez que la compañía los necesita. Solo lectura: se valida que sea una única sentencia
/// SELECT/WITH con el único parámetro :folio antes de guardarla o ejecutarla de prueba.
/// </summary>
public sealed class SapDocumentLookupService : ISapDocumentLookupService
{
    /// <summary>Alias obligatorios (minúsculas) del contrato de columnas.</summary>
    public static readonly IReadOnlyList<string> RequiredColumns = new[]
    {
        "doc_entry", "doc_num", "folio", "sii_document_type", "document_date", "customer_code",
        "customer_name", "customer_tax_id", "destination_address", "destination_commune", "customer_po",
        "base_order_entry", "units", "packages", "total_amount", "is_canceled",
    };

    private static readonly Regex ParameterRegex = new(@":(\w+)", RegexOptions.Compiled);

    // Palabras de escritura/ejecución rechazadas sobre el SQL ya sin comentarios, literales ni
    // identificadores entre comillas. REPLACE solo se rechaza si no es la función REPLACE( ... ).
    private static readonly Regex ForbiddenRegex = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|INTO|EXEC|EXECUTE|DROP|ALTER|CREATE|TRUNCATE|GRANT|REVOKE|CALL|UPSERT|DECLARE|OPENROWSET|OPENQUERY|OPENDATASOURCE)\b|\bREPLACE\b(?!\s*\()|\bxp_\w*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly IHanaService _hana;

    public SapDocumentLookupService(DespachosDbContext db, ICurrentCompanyAccessor company,
        ICurrentUserContext user, IHanaService hana)
    {
        _db = db;
        _company = company;
        _user = user;
        _hana = hana;
    }

    private Guid CompanyId => _company.CompanyId;

    /// <summary>Tipo de documento según el código SII: 33/34 factura, 52 guía de despacho/traslado.</summary>
    public static string DocumentKindFor(int siiType) => siiType switch
    {
        33 or 34 => DocumentKinds.Invoice,
        52 => DocumentKinds.TransferGuide,
        _ => throw new InvalidOperationException($"Tipo de documento SII no soportado: {siiType}."),
    };

    /// <summary>El motor de la plataforma es SAP HANA (cualquier valor que contenga "hana").</summary>
    public static bool IsHana(string? engineType) =>
        engineType?.Contains("hana", StringComparison.OrdinalIgnoreCase) == true;

    // ------------------------------------------------------------------ Búsqueda

    public async Task<SapDocumentInfo?> FindAsync(string documentKind, long folio, int? siiDocumentType = null, CancellationToken ct = default)
    {
        RequireKind(documentKind);
        var query = await GetQueryAsync(documentKind, ct);
        if (query is null || !query.IsActive)
            throw new InvalidOperationException(
                $"No hay consulta SAP configurada para {DocumentKinds.PluralLabel(documentKind)}. " +
                "Configúrela en Despachos > Configuración > Consultas SAP.");

        var rows = await _hana.QueryDynamicAsync(
            Executable(query.SqlText), new { folio, sii_document_type = siiDocumentType ?? 0 }, ct);
        if (rows.Count == 0) return null;

        var docs = new List<SapDocumentInfo>();
        foreach (var raw in rows)
        {
            var row = ToCaseInsensitive(raw);
            var missing = MissingColumns(row);
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"La consulta SAP de {DocumentKinds.PluralLabel(documentKind)} no devuelve las columnas obligatorias: {string.Join(", ", missing)}.");
            docs.Add(Map(documentKind, row));
        }

        if (siiDocumentType is { } wanted) docs = docs.Where(d => d.SiiDocumentType == wanted).ToList();
        var distinct = docs.GroupBy(d => d.DocEntry).Select(g => g.First()).ToList();
        if (distinct.Count == 0) return null;
        if (distinct.Count > 1)
        {
            var types = string.Join(", ", distinct.Select(d => d.SiiDocumentType).Distinct().OrderBy(t => t));
            throw new InvalidOperationException(
                $"El folio {folio} existe con más de un tipo de documento ({types}); indique el tipo.");
        }
        return distinct[0];
    }

    // ------------------------------------------------------------------ Validación

    public async Task<QueryValidationResult> ValidateQueryAsync(string sql, long sampleFolio) =>
        (await ProbeCoreAsync(null, sql, sampleFolio)).Validation;

    public Task<SapQueryProbeResult> ProbeAsync(string documentKind, string sql, long sampleFolio)
    {
        RequireKind(documentKind);
        return ProbeCoreAsync(documentKind, sql, sampleFolio);
    }

    private async Task<SapQueryProbeResult> ProbeCoreAsync(string? documentKind, string sql, long sampleFolio)
    {
        var staticError = CheckStatic(sql);
        if (staticError is not null) return Invalid(staticError);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            rows = await _hana.QueryDynamicAsync(Executable(sql), new { folio = sampleFolio, sii_document_type = 0 });
        }
        catch (Exception ex)
        {
            return Invalid($"SAP rechazó la consulta: {ex.Message}");
        }

        if (rows.Count == 0)
            return Invalid($"La consulta no devolvió filas para el folio {sampleFolio}; pruebe con un folio que exista en SAP.");

        var row = ToCaseInsensitive(rows[0]);
        var missing = MissingColumns(row);
        if (missing.Count > 0)
            return new SapQueryProbeResult(
                new QueryValidationResult(false, missing, "Faltan columnas obligatorias: " + string.Join(", ", missing) + "."), null);

        try
        {
            var doc = documentKind is null
                ? Build(DocumentKinds.Invoice, ToInt(row["sii_document_type"]), row)
                : Map(documentKind, row);
            return new SapQueryProbeResult(new QueryValidationResult(true, Array.Empty<string>(), null), doc);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or InvalidOperationException)
        {
            return Invalid($"Una columna tiene un tipo de dato inesperado: {ex.Message}");
        }
    }

    private static SapQueryProbeResult Invalid(string error) =>
        new(new QueryValidationResult(false, Array.Empty<string>(), error), null);

    private static List<string> MissingColumns(IReadOnlyDictionary<string, object?> row) =>
        RequiredColumns.Where(c => !row.ContainsKey(c)).ToList();

    /// <summary>
    /// Comprobaciones previas a ejecutar: una sola sentencia SELECT/WITH, sin ';' salvo uno
    /// final, y solo el parámetro :folio (que debe usarse). Devuelve el error o null.
    /// </summary>
    private static string? CheckStatic(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return "La consulta está vacía.";
        var clean = Sanitize(sql).Trim();
        if (clean.EndsWith(';')) clean = clean[..^1].TrimEnd();
        if (clean.Length == 0) return "La consulta está vacía.";
        if (clean.Contains(';')) return "Solo se permite una sentencia (sin ';' intermedios).";

        var firstWord = Regex.Match(clean, @"^\w+").Value;
        if (!firstWord.Equals("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !firstWord.Equals("WITH", StringComparison.OrdinalIgnoreCase))
            return "La consulta debe empezar con SELECT o WITH (solo lectura).";

        var names = ParameterRegex.Matches(clean).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var extra = names.Where(n => !n.Equals("folio", StringComparison.OrdinalIgnoreCase) &&
                                     !n.Equals("sii_document_type", StringComparison.OrdinalIgnoreCase)).ToList();
        if (extra.Count > 0) return $"Parámetro no permitido: :{extra[0]}. Solo se admiten :folio y :sii_document_type.";
        if (!names.Contains("folio", StringComparer.OrdinalIgnoreCase)) return "La consulta debe filtrar por el parámetro :folio.";

        var forbidden = ForbiddenRegex.Match(clean);
        if (forbidden.Success) return $"La consulta contiene una palabra no permitida (solo lectura): {forbidden.Value}.";
        return null;
    }

    /// <summary>
    /// Devuelve el SQL con comentarios y contenido de literales/identificadores entre comillas
    /// reemplazados por espacios, conservando el largo (así los índices coinciden con el original).
    /// </summary>
    private static string Sanitize(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') { sb.Append(' '); i++; }
                if (i < sql.Length) sb.Append('\n');
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? sql.Length : end + 2;
                for (; i < stop; i++) sb.Append(sql[i] == '\n' ? '\n' : ' ');
                i--;
            }
            else if (c == '\'' || c == '"' || c == '[')
            {
                var close = c == '[' ? ']' : c;
                sb.Append(c);
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == close)
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == close) { sb.Append("  "); i += 2; continue; }
                        break;
                    }
                    sb.Append(' ');
                    i++;
                }
                if (i < sql.Length) sb.Append(close);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>SQL a ejecutar: el original sin el ';' final (si lo hay).</summary>
    private static string Executable(string sql)
    {
        var text = sql.TrimEnd();
        var clean = Sanitize(text);
        return clean.EndsWith(';') ? text[..^1].TrimEnd() : text.Trim();
    }

    // ------------------------------------------------------------------ Mantención

    public async Task<IReadOnlyList<SapDocumentQuery>> ListQueriesAsync()
    {
        foreach (var kind in DocumentKinds.All) await GetQueryAsync(kind, CancellationToken.None);
        return await _db.SapDocumentQueries.AsNoTracking()
            .Where(q => q.CompanyId == CompanyId)
            .OrderBy(q => q.DocumentKind)
            .ToListAsync();
    }

    public async Task SaveQueryAsync(string documentKind, string sql, bool isActive, long sampleFolio)
    {
        RequireKind(documentKind);
        sql = (sql ?? "").Trim();
        var result = await ValidateQueryAsync(sql, sampleFolio);
        if (!result.IsValid)
            throw new InvalidOperationException("La consulta no se guardó. " + result.Error);

        var now = DateTime.UtcNow;
        var row = await _db.SapDocumentQueries
            .FirstOrDefaultAsync(q => q.CompanyId == CompanyId && q.DocumentKind == documentKind);
        if (row is null)
        {
            row = new SapDocumentQuery
            {
                CompanyId = CompanyId, DocumentKind = documentKind, CreatedAt = now, CreatedBy = _user.UserId,
            };
            _db.SapDocumentQueries.Add(row);
        }
        else
        {
            row.UpdatedAt = now;
            row.UpdatedBy = _user.UserId;
        }
        row.SqlText = sql;
        row.IsActive = isActive;
        row.ValidatedAt = now;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Consulta de la compañía para el tipo. Si no hay fila y el motor es HANA, inserta el
    /// seed embebido; con otro motor queda sin configurar (null).
    /// </summary>
    private async Task<SapDocumentQuery?> GetQueryAsync(string kind, CancellationToken ct)
    {
        var row = await _db.SapDocumentQueries
            .FirstOrDefaultAsync(q => q.CompanyId == CompanyId && q.DocumentKind == kind, ct);
        if (row is not null) return row;

        if (!IsHana(await _hana.GetPlatformEngineTypeAsync(ct))) return null;

        row = new SapDocumentQuery
        {
            CompanyId = CompanyId, DocumentKind = kind, SqlText = SapQuerySeeds.Get(kind),
            IsActive = true, CreatedAt = DateTime.UtcNow, CreatedBy = _user.UserId,
        };
        _db.SapDocumentQueries.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Otra solicitud la sembró primero (índice único): se usa la existente.
            _db.Entry(row).State = EntityState.Detached;
            return await _db.SapDocumentQueries
                .FirstOrDefaultAsync(q => q.CompanyId == CompanyId && q.DocumentKind == kind, ct);
        }
        return row;
    }

    private static void RequireKind(string kind)
    {
        if (!DocumentKinds.All.Contains(kind))
            throw new InvalidOperationException($"Tipo de documento inválido: '{kind}'.");
    }

    // ------------------------------------------------------------------ Mapeo

    private static Dictionary<string, object?> ToCaseInsensitive(IReadOnlyDictionary<string, object?> row) =>
        new(row, StringComparer.OrdinalIgnoreCase);

    private static SapDocumentInfo Map(string kind, IReadOnlyDictionary<string, object?> r)
    {
        var sii = ToInt(r["sii_document_type"]);
        if (DocumentKindFor(sii) != kind)
            throw new InvalidOperationException(
                $"La consulta SAP de {DocumentKinds.PluralLabel(kind)} devolvió un documento de tipo SII {sii}, que no corresponde a ese tipo; revise la consulta.");
        return Build(kind, sii, r);
    }

    private static SapDocumentInfo Build(string kind, int sii, IReadOnlyDictionary<string, object?> r) => new(
        DocumentKind: kind,
        SiiDocumentType: sii,
        Folio: ToLong(r["folio"]),
        DocEntry: ToInt(r["doc_entry"]),
        DocNum: ToInt(r["doc_num"]),
        DocumentDate: ToDate(r["document_date"]),
        CustomerCode: Str(r["customer_code"]) ?? "",
        CustomerName: Str(r["customer_name"]) ?? "",
        CustomerTaxId: Str(r["customer_tax_id"]),
        DestinationAddress: Str(r["destination_address"]),
        DestinationCommune: Str(r["destination_commune"]),
        CustomerPo: Str(r["customer_po"]),
        BaseOrderEntry: IsNull(r["base_order_entry"]) ? null : ToInt(r["base_order_entry"]),
        Units: ToDecimal(r["units"]),
        Packages: ToInt(r["packages"]),
        TotalAmount: ToDecimal(r["total_amount"]),
        IsCanceled: ToBool(r["is_canceled"]),
        WeightKg: Optional(r, "weight_kg"),
        VolumeM3: Optional(r, "volume_m3"));

    private static decimal? Optional(IReadOnlyDictionary<string, object?> r, string key) =>
        r.TryGetValue(key, out var v) && !IsNull(v) ? ToDecimal(v) : null;

    private static bool IsNull(object? v) => v is null or DBNull;

    private static string? Str(object? v)
    {
        if (IsNull(v)) return null;
        var s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    // Los números nulos de SAP (SUM sin filas, LEFT JOIN vacío) cuentan como 0.
    private static int ToInt(object? v) => IsNull(v) ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    private static long ToLong(object? v) => IsNull(v) ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture);
    private static decimal ToDecimal(object? v) => IsNull(v) ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);

    private static DateOnly ToDate(object? v) => v switch
    {
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        DateTimeOffset dto => DateOnly.FromDateTime(dto.DateTime),
        string s => DateOnly.FromDateTime(DateTime.Parse(s, CultureInfo.InvariantCulture)),
        _ => throw new FormatException("document_date no es una fecha."),
    };

    private static bool ToBool(object? v) => v switch
    {
        null or DBNull => false,
        bool b => b,
        string s => s.Trim().ToUpperInvariant() is "Y" or "S" or "1" or "TRUE",
        _ => Convert.ToDecimal(v, CultureInfo.InvariantCulture) != 0m,
    };
}
