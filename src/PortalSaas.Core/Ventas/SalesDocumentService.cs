using System.Text.Json;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Ventas;

/// <summary>
/// Motor genérico de documentos de venta -- generaliza el antiguo SalesOrderService
/// (una Orden de Venta es hoy solo SalesDocumentType.SalesOrder). Tabla HANA/recurso
/// Service Layer/filtros extra resueltos vía SalesDocumentTypeCatalog, portado de
/// GenericoVentaService en referencia-original/PortalSAP_v2 -- ver el doc-comment del
/// catálogo para el porqué de los filtros extra (tipos que comparten tabla/recurso SAP).
/// </summary>
public sealed class SalesDocumentService : ISalesDocumentService
{
    private readonly IHanaService _hana;
    private readonly ISapConnectionProvider _connectionProvider;

    public SalesDocumentService(IHanaService hana, ISapConnectionProvider connectionProvider)
    {
        _hana = hana;
        _connectionProvider = connectionProvider;
    }

    public Task<bool> CanCreateAsync(SalesDocumentType type, CancellationToken ct = default) =>
        Task.FromResult(SalesDocumentTypeCatalog.Resolve(type).DefaultCanCreate);

    public async Task<int> CreateAsync(SalesDocumentType type, string portalUsername, SalesDocumentDto document, CancellationToken ct = default)
    {
        var entry = SalesDocumentTypeCatalog.Resolve(type);

        var header = new SapSalesDocumentHeader
        {
            CardCode = document.CustomerCardCode,
            CardName = document.CustomerName,
            SalesPersonCode = document.SalesEmployeeCode,
            Comments = document.Comments,
            DocDate = document.DocDate.ToDateTime(TimeOnly.MinValue),
            DocDueDate = document.DocDueDate.ToDateTime(TimeOnly.MinValue),
            TaxDate = document.TaxDate.ToDateTime(TimeOnly.MinValue),
            NumAtCard = document.CustomerReferenceNumber,
            U_PortalUser = portalUsername,
            DocumentLines = document.Lines.Select(line => new SapSalesDocumentLine
            {
                ItemCode = line.ItemCode,
                ItemDescription = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                WarehouseCode = line.WarehouseCode,
            }).ToList(),
        };

        var session = await _connectionProvider.GetConnectionAsync(ct);
        var created = await session.PostAsync<SapSalesDocumentHeader>(entry.Resource, BuildRequestBody(header, entry), ct)
            ?? throw new InvalidOperationException("Service Layer no devolvió el documento creado.");

        return created.DocEntry ?? throw new InvalidOperationException("El documento se creó sin DocEntry.");
    }

    public async Task<SalesDocumentDto?> GetAsync(SalesDocumentType type, int docEntry, CancellationToken ct = default)
    {
        var entry = SalesDocumentTypeCatalog.Resolve(type);
        var session = await _connectionProvider.GetConnectionAsync(ct);
        var sap = await session.GetAsync<SapSalesDocumentHeader>($"{entry.Resource}({docEntry})", ct: ct);
        if (sap is null)
        {
            return null;
        }

        return new SalesDocumentDto(
            CustomerCardCode: sap.CardCode,
            CustomerName: sap.CardName,
            SalesEmployeeCode: sap.SalesPersonCode,
            Comments: sap.Comments,
            DocDate: DateOnly.FromDateTime(sap.DocDate ?? DateTime.Today),
            DocDueDate: DateOnly.FromDateTime(sap.DocDueDate ?? DateTime.Today),
            TaxDate: DateOnly.FromDateTime(sap.TaxDate ?? DateTime.Today),
            CustomerReferenceNumber: sap.NumAtCard,
            Lines: sap.DocumentLines.Select(line => new SalesDocumentLineDto(
                ItemCode: line.ItemCode,
                Description: line.ItemDescription,
                Quantity: line.Quantity,
                UnitPrice: line.UnitPrice,
                DiscountPercent: line.DiscountPercent,
                WarehouseCode: line.WarehouseCode)).ToList(),
            DocEntry: sap.DocEntry,
            DocNum: sap.DocNum,
            DocTotal: sap.DocTotal,
            Status: sap.DocumentStatus == "bost_Close" ? "Cerrado" : "Abierto");
    }

    public async Task<SalesDocumentListResult> ListAsync(SalesDocumentType type, SalesDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var entry = SalesDocumentTypeCatalog.Resolve(type);
        var clampedPageSize = pageSize switch { 50 => 50, 100 => 100, _ => 25 };
        var offset = Math.Max(0, page - 1) * clampedPageSize;

        var (whereClause, parameters) = BuildWhereClause(filter, entry.ExtraFilters);

        var countSql = $"""SELECT COUNT(*) FROM "{entry.Table}" o {whereClause}""";
        var totalRecords = (await _hana.QueryAsync<int>(countSql, parameters, ct)).FirstOrDefault();

        var listSql = $"""
            SELECT o."DocEntry", o."DocNum", o."CardCode" AS "CustomerCardCode",
                   o."CardName" AS "CustomerName", o."DocDate", o."DocTotal",
                   o."NumAtCard" AS "CustomerReferenceNumber", s."SlpName" AS "SalesEmployeeName",
                   CASE WHEN o."DocStatus" = 'O' THEN 'Abierto' ELSE 'Cerrado' END AS "Status"
            FROM "{entry.Table}" o
            LEFT JOIN "OSLP" s ON s."SlpCode" = o."SlpCode"
            {whereClause}
            ORDER BY o."DocEntry" DESC
            LIMIT {clampedPageSize} OFFSET {offset}
            """;

        var items = await _hana.QueryAsync<SalesDocumentSummaryDto>(listSql, parameters, ct);
        return new SalesDocumentListResult(items, totalRecords);
    }

    /// <summary>
    /// Tipos que comparten tabla/recurso SAP (ej. Factura Deudores/Reserva/Recibo, las
    /// tres "OINV"/"Invoices") necesitan fijar en el POST la misma columna que ListAsync
    /// usa para distinguirlas al leer -- si no, Service Layer crea el documento con el
    /// valor por defecto (ej. una Factura Reserva quedaría como Factura Deudores común,
    /// silenciosamente mal). Solo los filtros de igualdad se fijan -- portado de
    /// GenericoVentaService.CrearAsync en la referencia.
    /// </summary>
    private static object BuildRequestBody(SapSalesDocumentHeader header, SalesDocumentTypeCatalog.Entry entry)
    {
        var equalFilters = entry.ExtraFilters.Where(f => f.IsEqual).ToList();
        if (equalFilters.Count == 0)
        {
            return header;
        }

        var flattened = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(header))!;
        foreach (var filter in equalFilters)
        {
            flattened[filter.Column] = filter.Value;
        }

        return flattened;
    }

    /// <summary>
    /// Arma el WHERE dinámicamente según qué filtros vinieron -- todos opcionales, así
    /// que se reusa el mismo WHERE (y el mismo diccionario de parámetros) para el
    /// COUNT(*) y el SELECT paginado, nunca concatenando el valor del usuario, solo los
    /// nombres de los parámetros (que son fijos, no vienen del usuario). extraFilters
    /// del catálogo (no del usuario) se agregan siempre, con "=" o "&lt;&gt;" según
    /// IsEqual -- portado de GenericoVentaService.ListarAsync.
    /// </summary>
    private static (string WhereClause, IReadOnlyDictionary<string, object?> Parameters) BuildWhereClause(
        SalesDocumentFilter? filter, IReadOnlyList<SalesDocumentTypeCatalog.ExtraFilter> extraFilters)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object?>();

        if (filter is not null)
        {
            if (filter.DateFrom is { } dateFrom)
            {
                clauses.Add("""o."DocDate" >= :dateFrom""");
                parameters["dateFrom"] = dateFrom.ToDateTime(TimeOnly.MinValue);
            }

            if (filter.DateTo is { } dateTo)
            {
                clauses.Add("""o."DocDate" <= :dateTo""");
                parameters["dateTo"] = dateTo.ToDateTime(TimeOnly.MinValue);
            }

            if (!string.IsNullOrWhiteSpace(filter.CustomerCardCode))
            {
                clauses.Add("""o."CardCode" = :customerCardCode""");
                parameters["customerCardCode"] = filter.CustomerCardCode.Trim();
            }

            if (!string.IsNullOrWhiteSpace(filter.CustomerName))
            {
                clauses.Add("""o."CardName" LIKE :customerName""");
                parameters["customerName"] = $"%{filter.CustomerName.Trim()}%";
            }

            if (filter.DocNum is { } docNum)
            {
                clauses.Add("""o."DocNum" = :docNum""");
                parameters["docNum"] = docNum;
            }
        }

        for (var i = 0; i < extraFilters.Count; i++)
        {
            var extraFilter = extraFilters[i];
            var paramName = $"extraFilter{i}";
            var op = extraFilter.IsEqual ? "=" : "<>";
            clauses.Add($"""o."{extraFilter.Column}" {op} :{paramName}""");
            parameters[paramName] = extraFilter.Value;
        }

        var whereClause = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
        return (whereClause, parameters);
    }
}
