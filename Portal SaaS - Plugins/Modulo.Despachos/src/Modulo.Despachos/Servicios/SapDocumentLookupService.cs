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

    /// <summary>Contrato de la consulta de líneas de factura (parámetro :doc_entry).</summary>
    public static readonly IReadOnlyList<string> LineColumns = new[]
    {
        "line_num", "item_code", "item_name", "quantity", "unit", "weight_kg", "customer_activity", "destination_city",
    };

    /// <summary>Contrato de las consultas de transferencias (por LPN y pendientes).</summary>
    public static readonly IReadOnlyList<string> TransferColumns = new[]
    {
        "doc_entry", "doc_num", "document_date", "warehouse_code", "warehouse_name", "destination_address",
        "destination_commune", "units", "is_canceled", "lpn",
    };

    /// <summary>Contrato de las consultas de Entregas (pendientes del cliente y por LPN).</summary>
    public static readonly IReadOnlyList<string> DeliveryColumns = new[]
    {
        "doc_entry", "doc_num", "document_date", "customer_code", "customer_name", "customer_tax_id", "destination_address",
        "destination_commune", "lpn", "customer_po", "order_date", "po_count", "units", "net_amount", "is_open",
    };

    /// <summary>Contrato de la consulta de bodegas.</summary>
    public static readonly IReadOnlyList<string> WarehouseColumns = new[] { "warehouse_code", "warehouse_name", "address", "commune" };

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

    public async Task<SapDocumentLines> GetLinesAsync(string queryKind, int docEntry, CancellationToken ct = default)
    {
        if (queryKind is not (SapQueryKinds.InvoiceLines or SapQueryKinds.TransferLines or SapQueryKinds.DeliveryLines))
            throw new InvalidOperationException($"Consulta de líneas inválida: '{queryKind}'.");
        var query = await RequireActiveQueryAsync(queryKind, ct);

        var rows = await _hana.QueryDynamicAsync(Executable(query.SqlText), new { doc_entry = docEntry }, ct);
        var mapped = rows.Select(ToCaseInsensitive).ToList();
        var missing = mapped.Count == 0 ? new List<string>() : LineColumns.Where(c => !mapped[0].ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"La consulta SAP «{SapQueryKinds.Label(queryKind)}» no devuelve las columnas obligatorias: {string.Join(", ", missing)}.");
        return MapLines(mapped);
    }

    public async Task<SapTransferInfo?> FindTransferByLpnAsync(string lpn, CancellationToken ct = default)
    {
        var list = await QueryTransfersAsync(SapQueryKinds.TransferByLpn, new { lpn }, ct);
        var distinct = list.GroupBy(t => t.DocEntry).Select(g => g.First()).ToList();
        // Transferencia anulada y recreada con el mismo LPN: vale la vigente.
        if (distinct.Count > 1 && distinct.Count(t => !t.IsCanceled) == 1) return distinct.Single(t => !t.IsCanceled);
        if (distinct.Count > 1)
            throw new InvalidOperationException($"El LPN {lpn} está en más de una transferencia en SAP; revise el LPN.");
        return distinct.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SapTransferInfo>> ListPendingTransfersAsync(string warehouseCode, DateOnly fromDate, CancellationToken ct = default) =>
        (await QueryTransfersAsync(SapQueryKinds.TransferPending,
            new { warehouse = warehouseCode, from_date = fromDate.ToDateTime(TimeOnly.MinValue) }, ct))
        .GroupBy(t => t.DocEntry).Select(g => g.First()).ToList();

    public async Task<IReadOnlyList<SapTransferInfo>> ListPendingTransfersAllAsync(DateOnly fromDate, CancellationToken ct = default) =>
        (await QueryTransfersAsync(SapQueryKinds.TransferPendingAll,
            new { from_date = fromDate.ToDateTime(TimeOnly.MinValue) }, ct))
        .GroupBy(t => t.DocEntry).Select(g => g.First()).ToList();

    public async Task<IReadOnlyList<SapDeliveryInfo>> ListPendingDeliveriesAsync(string customerCode, DateOnly fromDate, CancellationToken ct = default) =>
        (await QueryDeliveriesAsync(SapQueryKinds.DeliveryPending,
            new { customer = customerCode, from_date = fromDate.ToDateTime(TimeOnly.MinValue) }, ct))
        .GroupBy(d => d.DocEntry).Select(g => g.First()).ToList();

    public async Task<SapDeliveryInfo?> FindDeliveryByLpnAsync(string lpn, CancellationToken ct = default)
    {
        var distinct = (await QueryDeliveriesAsync(SapQueryKinds.DeliveryByLpn, new { lpn }, ct))
            .GroupBy(d => d.DocEntry).Select(g => g.First()).ToList();
        // Entrega anulada o cerrada y otra vigente con el mismo LPN: vale la abierta.
        if (distinct.Count > 1 && distinct.Count(d => d.IsOpen) == 1) return distinct.Single(d => d.IsOpen);
        if (distinct.Count > 1)
            throw new InvalidOperationException($"El LPN {lpn} está en más de una Entrega en SAP; revise el LPN.");
        return distinct.FirstOrDefault();
    }

    private async Task<List<SapDeliveryInfo>> QueryDeliveriesAsync(string kind, object parameters, CancellationToken ct)
    {
        var query = await RequireActiveQueryAsync(kind, ct);
        var rows = (await _hana.QueryDynamicAsync(Executable(query.SqlText), parameters, ct)).Select(ToCaseInsensitive).ToList();
        RequireColumns(kind, rows, DeliveryColumns);
        return rows.Select(MapDelivery).ToList();
    }

    private static SapDeliveryInfo MapDelivery(IReadOnlyDictionary<string, object?> r) => new(
        DocEntry: ToInt(r["doc_entry"]),
        DocNum: ToInt(r["doc_num"]),
        DocumentDate: ToDate(r["document_date"]),
        CustomerCode: Str(r["customer_code"]) ?? "",
        CustomerName: Str(r["customer_name"]) ?? "",
        CustomerTaxId: Str(r["customer_tax_id"]),
        DestinationAddress: Str(r["destination_address"]),
        DestinationCommune: Str(r["destination_commune"]),
        Lpn: Str(r["lpn"]),
        CustomerPo: Str(r["customer_po"]),
        OrderDate: IsNull(r["order_date"]) ? null : ToDate(r["order_date"]),
        PoCount: ToInt(r["po_count"]),
        Units: ToDecimal(r["units"]),
        NetAmount: ToDecimal(r["net_amount"]),
        IsOpen: ToBool(r["is_open"]));

    public async Task<IReadOnlyList<SapWarehouse>> ListSapWarehousesAsync(CancellationToken ct = default)
    {
        var query = await RequireActiveQueryAsync(SapQueryKinds.Warehouses, ct);
        var rows = (await _hana.QueryDynamicAsync(Executable(query.SqlText), null, ct)).Select(ToCaseInsensitive).ToList();
        RequireColumns(SapQueryKinds.Warehouses, rows, WarehouseColumns);
        return rows.Select(MapWarehouse).ToList();
    }

    public async Task<SapBusinessPartner?> FindBusinessPartnerAsync(string taxId, CancellationToken ct = default)
    {
        if (!Dominio.RutChileno.IsValid(taxId)) throw new InvalidOperationException($"El RUT «{taxId}» no es válido.");
        var query = await RequireActiveQueryAsync(SapQueryKinds.BusinessPartnerByTaxId, ct);
        var rows = (await _hana.QueryDynamicAsync(Executable(query.SqlText), new { tax_id = Dominio.RutChileno.Normalize(taxId) }, ct))
            .Select(ToCaseInsensitive).ToList();
        RequireColumns(SapQueryKinds.BusinessPartnerByTaxId, rows, PartnerColumns);
        return MapPartner(rows);
    }

    private static readonly string[] PartnerColumns =
        { "card_code", "card_name", "activity", "is_active", "address_name", "address", "commune", "city" };

    /// <summary>Filas = una por dirección (cabecera repetida). Varios socios: el activo; si no, el primero por código.</summary>
    private static SapBusinessPartner? MapPartner(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var partners = rows.GroupBy(r => Str(r["card_code"]) ?? "").Where(g => g.Key.Length > 0).ToList();
        if (partners.Count == 0) return null;
        var chosen = partners.OrderByDescending(g => ToBool(g.First()["is_active"])).ThenBy(g => g.Key, StringComparer.Ordinal).First();
        var head = chosen.First();
        var addresses = chosen.Where(r => Str(r["address"]) is not null)
            .Select(r => new SapPartnerAddress(Str(r["address_name"]), Str(r["address"]), Str(r["commune"]), Str(r["city"])))
            .ToList();
        return new SapBusinessPartner(chosen.Key, Str(head["card_name"]) ?? "", Str(head["activity"]), ToBool(head["is_active"]), addresses);
    }

    private async Task<SapQueryProbeResult> ProbePartnerAsync(string sql, string? sampleTaxId)
    {
        var staticError = CheckStatic(sql, PartnerParameters);
        if (staticError is not null) return Invalid(staticError);
        if (!Dominio.RutChileno.IsValid(sampleTaxId)) return Invalid("Indique un RUT de prueba válido (de un cliente que exista en SAP).");
        var (rows, error) = await TryRunAsync(sql, new { tax_id = Dominio.RutChileno.Normalize(sampleTaxId!) });
        if (error is not null) return Invalid(error);
        if (rows!.Count == 0) return Invalid($"La consulta no devolvió socios para el RUT {sampleTaxId}; pruebe con otro.");
        var missing = PartnerColumns.Where(c => !rows[0].ContainsKey(c)).ToList();
        if (missing.Count > 0) return MissingResult(missing);
        return new SapQueryProbeResult(Valid, null, Partner: MapPartner(rows));
    }

    private async Task<List<SapTransferInfo>> QueryTransfersAsync(string kind, object parameters, CancellationToken ct)
    {
        var query = await RequireActiveQueryAsync(kind, ct);
        var rows = (await _hana.QueryDynamicAsync(Executable(query.SqlText), parameters, ct)).Select(ToCaseInsensitive).ToList();
        RequireColumns(kind, rows, TransferColumns);
        return rows.Select(MapTransfer).ToList();
    }

    private async Task<SapDocumentQuery> RequireActiveQueryAsync(string kind, CancellationToken ct)
    {
        var query = await GetQueryAsync(kind, ct);
        if (query is null || !query.IsActive)
            throw new InvalidOperationException(
                $"Configure la consulta SAP «{SapQueryKinds.Label(kind)}» en Despachos > Configuración > Consultas SAP.");
        return query;
    }

    private static void RequireColumns(string kind, IReadOnlyList<Dictionary<string, object?>> rows, IReadOnlyList<string> columns)
    {
        var missing = rows.Count == 0 ? new List<string>() : columns.Where(c => !rows[0].ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"La consulta SAP «{SapQueryKinds.Label(kind)}» no devuelve las columnas obligatorias: {string.Join(", ", missing)}.");
    }

    private static SapTransferInfo MapTransfer(IReadOnlyDictionary<string, object?> r) => new(
        DocEntry: ToInt(r["doc_entry"]),
        DocNum: ToInt(r["doc_num"]),
        DocumentDate: ToDate(r["document_date"]),
        WarehouseCode: Str(r["warehouse_code"]) ?? "",
        WarehouseName: Str(r["warehouse_name"]) ?? "",
        DestinationAddress: Str(r["destination_address"]),
        DestinationCommune: Str(r["destination_commune"]),
        Units: ToDecimal(r["units"]),
        IsCanceled: ToBool(r["is_canceled"]),
        Lpn: Str(r["lpn"]));

    private static SapWarehouse MapWarehouse(IReadOnlyDictionary<string, object?> r) => new(
        Str(r["warehouse_code"]) ?? "", Str(r["warehouse_name"]) ?? "", Str(r["address"]), Str(r["commune"]));

    // ------------------------------------------------------------------ Validación

    public async Task<QueryValidationResult> ValidateQueryAsync(string sql, long sampleFolio) =>
        (await ProbeCoreAsync(null, sql, sampleFolio)).Validation;

    public Task<SapQueryProbeResult> ProbeAsync(string documentKind, string sql, long sampleFolio, string? sampleText = null)
    {
        RequireQueryKind(documentKind);
        return documentKind switch
        {
            SapQueryKinds.InvoiceLines or SapQueryKinds.TransferLines or SapQueryKinds.DeliveryLines =>
                ProbeLinesAsync(sql, checked((int)sampleFolio)),
            SapQueryKinds.DeliveryPending => ProbeDeliveriesAsync(sql, DeliveryPendingParameters,
                new { customer = (sampleText ?? "").Trim(), from_date = DateTime.Today.AddDays(-30) },
                $"el cliente {sampleText} en los últimos 30 días"),
            SapQueryKinds.DeliveryByLpn => ProbeDeliveriesAsync(sql, LpnParameters, new { lpn = (sampleText ?? "").Trim() },
                $"el LPN {sampleText}"),
            SapQueryKinds.TransferByLpn => ProbeTransfersAsync(sql, LpnParameters, new { lpn = (sampleText ?? "").Trim() },
                $"el LPN {sampleText}"),
            SapQueryKinds.TransferPending => ProbeTransfersAsync(sql, PendingParameters,
                new { warehouse = (sampleText ?? "").Trim(), from_date = DateTime.Today.AddDays(-30) },
                $"la bodega {sampleText} en los últimos 30 días"),
            SapQueryKinds.TransferPendingAll => ProbeTransfersAsync(sql, PendingAllParameters,
                new { from_date = DateTime.Today.AddDays(-30) }, "los últimos 30 días"),
            SapQueryKinds.Warehouses => ProbeWarehousesAsync(sql),
            SapQueryKinds.BusinessPartnerByTaxId => ProbePartnerAsync(sql, sampleText),
            _ => ProbeCoreAsync(documentKind, sql, sampleFolio),
        };
    }

    private async Task<SapQueryProbeResult> ProbeTransfersAsync(
        string sql, (string? Required, string[] Allowed) parameters, object values, string sampleLabel)
    {
        var staticError = CheckStatic(sql, parameters);
        if (staticError is not null) return Invalid(staticError);
        var (rows, error) = await TryRunAsync(sql, values);
        if (error is not null) return Invalid(error);
        if (rows!.Count == 0) return Invalid($"La consulta no devolvió transferencias para {sampleLabel}; pruebe con otra muestra.");
        var missing = TransferColumns.Where(c => !rows[0].ContainsKey(c)).ToList();
        if (missing.Count > 0) return MissingResult(missing);
        try
        {
            return new SapQueryProbeResult(Valid, null, Transfers: rows.Select(MapTransfer).ToList());
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return Invalid($"Una columna tiene un tipo de dato inesperado: {ex.Message}");
        }
    }

    private async Task<SapQueryProbeResult> ProbeDeliveriesAsync(
        string sql, (string? Required, string[] Allowed) parameters, object values, string sampleLabel)
    {
        var staticError = CheckStatic(sql, parameters);
        if (staticError is not null) return Invalid(staticError);
        var (rows, error) = await TryRunAsync(sql, values);
        if (error is not null) return Invalid(error);
        if (rows!.Count == 0) return Invalid($"La consulta no devolvió Entregas para {sampleLabel}; pruebe con otra muestra.");
        var missing = DeliveryColumns.Where(c => !rows[0].ContainsKey(c)).ToList();
        if (missing.Count > 0) return MissingResult(missing);
        try
        {
            return new SapQueryProbeResult(Valid, null, Deliveries: rows.Select(MapDelivery).ToList());
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return Invalid($"Una columna tiene un tipo de dato inesperado: {ex.Message}");
        }
    }

    private async Task<SapQueryProbeResult> ProbeWarehousesAsync(string sql)
    {
        var staticError = CheckStatic(sql, NoParameters);
        if (staticError is not null) return Invalid(staticError);
        var (rows, error) = await TryRunAsync(sql, null);
        if (error is not null) return Invalid(error);
        if (rows!.Count == 0) return Invalid("La consulta no devolvió bodegas.");
        var missing = WarehouseColumns.Where(c => !rows[0].ContainsKey(c)).ToList();
        if (missing.Count > 0) return MissingResult(missing);
        return new SapQueryProbeResult(Valid, null, Warehouses: rows.Select(MapWarehouse).ToList());
    }

    private async Task<(List<Dictionary<string, object?>>? Rows, string? Error)> TryRunAsync(string sql, object? values)
    {
        try
        {
            return ((await _hana.QueryDynamicAsync(Executable(sql), values)).Select(ToCaseInsensitive).ToList(), null);
        }
        catch (Exception ex)
        {
            return (null, $"SAP rechazó la consulta: {ex.Message}");
        }
    }

    private static readonly QueryValidationResult Valid = new(true, Array.Empty<string>(), null);

    private static SapQueryProbeResult MissingResult(List<string> missing) =>
        new(new QueryValidationResult(false, missing, "Faltan columnas obligatorias: " + string.Join(", ", missing) + "."), null);

    /// <summary>"Probar" de la consulta de líneas: <paramref name="sampleDocEntry"/> es un DocEntry de factura.</summary>
    private async Task<SapQueryProbeResult> ProbeLinesAsync(string sql, int sampleDocEntry)
    {
        var staticError = CheckStatic(sql, LinesParameter);
        if (staticError is not null) return Invalid(staticError);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            rows = await _hana.QueryDynamicAsync(Executable(sql), new { doc_entry = sampleDocEntry });
        }
        catch (Exception ex)
        {
            return Invalid($"SAP rechazó la consulta: {ex.Message}");
        }
        if (rows.Count == 0)
            return Invalid($"La consulta no devolvió líneas para el DocEntry {sampleDocEntry}; pruebe con una factura que exista en SAP.");

        var mapped = rows.Select(ToCaseInsensitive).ToList();
        var missing = LineColumns.Where(c => !mapped[0].ContainsKey(c)).ToList();
        if (missing.Count > 0)
            return new SapQueryProbeResult(
                new QueryValidationResult(false, missing, "Faltan columnas obligatorias: " + string.Join(", ", missing) + "."), null);
        try
        {
            return new SapQueryProbeResult(new QueryValidationResult(true, Array.Empty<string>(), null), null, MapLines(mapped));
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return Invalid($"Una columna tiene un tipo de dato inesperado: {ex.Message}");
        }
    }

    private async Task<SapQueryProbeResult> ProbeCoreAsync(string? documentKind, string sql, long sampleFolio)
    {
        var staticError = CheckStatic(sql, FolioParameters);
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

    /// <summary>Parámetros de las consultas por folio: :folio (obligatorio) y :sii_document_type.</summary>
    private static readonly (string? Required, string[] Allowed) FolioParameters = ("folio", new[] { "folio", "sii_document_type" });

    /// <summary>Parámetro de la consulta de líneas: :doc_entry.</summary>
    private static readonly (string? Required, string[] Allowed) LinesParameter = ("doc_entry", new[] { "doc_entry" });

    /// <summary>Transferencias: por LPN (:lpn) y pendientes (:warehouse obligatorio, :from_date); bodegas sin parámetros.</summary>
    private static readonly (string? Required, string[] Allowed) LpnParameters = ("lpn", new[] { "lpn" });
    private static readonly (string? Required, string[] Allowed) PendingParameters = ("warehouse", new[] { "warehouse", "from_date" });
    private static readonly (string? Required, string[] Allowed) PendingAllParameters = ("from_date", new[] { "from_date" });
    private static readonly (string? Required, string[] Allowed) NoParameters = (null, Array.Empty<string>());
    private static readonly (string? Required, string[] Allowed) PartnerParameters = ("tax_id", new[] { "tax_id" });
    /// <summary>Entregas pendientes: :customer (obligatorio) y :from_date; la de LPN usa LpnParameters.</summary>
    private static readonly (string? Required, string[] Allowed) DeliveryPendingParameters = ("customer", new[] { "customer", "from_date" });

    /// <summary>
    /// Comprobaciones previas a ejecutar: una sola sentencia SELECT/WITH, sin ';' salvo uno
    /// final, y solo los parámetros permitidos (el obligatorio debe usarse). Devuelve el error o null.
    /// </summary>
    private static string? CheckStatic(string sql, (string? Required, string[] Allowed) parameters)
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
        var extra = names.Where(n => !parameters.Allowed.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        if (extra.Count > 0)
            return parameters.Allowed.Length == 0
                ? $"Parámetro no permitido: :{extra[0]}. Esta consulta no usa parámetros."
                : $"Parámetro no permitido: :{extra[0]}. Solo se admiten {string.Join(" y ", parameters.Allowed.Select(a => ":" + a))}.";
        if (parameters.Required is not null && !names.Contains(parameters.Required, StringComparer.OrdinalIgnoreCase))
            return $"La consulta debe filtrar por el parámetro :{parameters.Required}.";

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
    /// <remarks>
    /// Se ejecuta sin comentarios: el servicio HANA enlaza los parámetros por posición de cada ":nombre" en el
    /// texto, incluidos los de un comentario ("-- filtra por :warehouse"), lo que corría los valores (el filtro de
    /// fecha recibía el código de bodega).
    /// </remarks>
    private static string Executable(string sql)
    {
        var text = StripComments(sql).TrimEnd();
        var clean = Sanitize(text);
        return clean.EndsWith(';') ? text[..^1].TrimEnd() : text.Trim();
    }

    /// <summary>Quita los comentarios -- y /* */ conservando intacto el texto entre comillas simples o dobles.</summary>
    private static string StripComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                if (i < sql.Length) sb.Append('\n');
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 1;
                sb.Append(' ');
            }
            else if (c is '\'' or '"')
            {
                sb.Append(c);
                for (i++; i < sql.Length; i++)
                {
                    sb.Append(sql[i]);
                    if (sql[i] != c) continue;
                    if (i + 1 < sql.Length && sql[i + 1] == c) { sb.Append(c); i++; continue; }
                    break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ Mantención

    public async Task<IReadOnlyList<SapDocumentQuery>> ListQueriesAsync()
    {
        foreach (var kind in SapQueryKinds.All) await GetQueryAsync(kind, CancellationToken.None);
        return await _db.SapDocumentQueries.AsNoTracking()
            .Where(q => q.CompanyId == CompanyId)
            .OrderBy(q => q.DocumentKind)
            .ToListAsync();
    }

    public async Task SaveQueryAsync(string documentKind, string sql, bool isActive, long sampleFolio, string? sampleText = null)
    {
        RequireQueryKind(documentKind);
        sql = (sql ?? "").Trim();
        var result = (await ProbeAsync(documentKind, sql, sampleFolio, sampleText)).Validation;
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

    private static void RequireQueryKind(string kind)
    {
        if (!SapQueryKinds.All.Contains(kind))
            throw new InvalidOperationException($"Tipo de consulta inválido: '{kind}'.");
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

    private static SapDocumentLines MapLines(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var lines = rows.Select(r => new SapDocumentLine(
            LineNum: ToInt(r["line_num"]),
            ItemCode: Str(r["item_code"]) ?? "",
            ItemName: Str(r["item_name"]) ?? "",
            Quantity: ToDecimal(r["quantity"]),
            Unit: Str(r["unit"]),
            WeightKg: IsNull(r["weight_kg"]) ? null : ToDecimal(r["weight_kg"]),
            ModelCode: OptionalStr(r, "model_code"),
            ModelName: OptionalStr(r, "model_name"),
            Amount: Optional(r, "line_amount"))).ToList();
        var first = rows.Count > 0 ? rows[0] : null;
        return new SapDocumentLines(
            first is null ? null : Str(first["customer_activity"]),
            first is null ? null : Str(first["destination_city"]),
            lines);
    }

    private static string? OptionalStr(IReadOnlyDictionary<string, object?> r, string key) =>
        r.TryGetValue(key, out var v) && !IsNull(v) && Str(v) is { Length: > 0 } s ? s : null;

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
