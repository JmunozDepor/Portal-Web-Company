using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Ventas;

/// <summary>
/// Órdenes de Venta (SAP ORDR/Orders) de la compañía activa -- digitación directa.
/// Tabla HANA y recurso de Service Layer fijos (un solo valor, no un diccionario de
/// configuración por tipo como GenericoVentaService en la referencia) porque esta
/// primera entrega solo soporta un tipo de documento -- ver CLAUDE.md "Qué se corta".
/// </summary>
public sealed class SalesOrderService : ISalesOrderService
{
    private readonly IHanaService _hana;
    private readonly ISapConnectionProvider _connectionProvider;

    public SalesOrderService(IHanaService hana, ISapConnectionProvider connectionProvider)
    {
        _hana = hana;
        _connectionProvider = connectionProvider;
    }

    public Task<bool> CanCreateAsync(CancellationToken ct = default) => Task.FromResult(true);

    public async Task<int> CreateAsync(string portalUsername, SalesOrderDto document, CancellationToken ct = default)
    {
        var header = new SapSalesOrderHeader
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
            DocumentLines = document.Lines.Select(line => new SapSalesOrderLine
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
        var created = await session.PostAsync<SapSalesOrderHeader>("Orders", header, ct)
            ?? throw new InvalidOperationException("Service Layer no devolvió el documento creado.");

        return created.DocEntry ?? throw new InvalidOperationException("El documento se creó sin DocEntry.");
    }

    public async Task<SalesOrderDto?> GetAsync(int docEntry, CancellationToken ct = default)
    {
        var session = await _connectionProvider.GetConnectionAsync(ct);
        var sap = await session.GetAsync<SapSalesOrderHeader>($"Orders({docEntry})", ct: ct);
        if (sap is null)
        {
            return null;
        }

        return new SalesOrderDto(
            CustomerCardCode: sap.CardCode,
            CustomerName: sap.CardName,
            SalesEmployeeCode: sap.SalesPersonCode,
            Comments: sap.Comments,
            DocDate: DateOnly.FromDateTime(sap.DocDate ?? DateTime.Today),
            DocDueDate: DateOnly.FromDateTime(sap.DocDueDate ?? DateTime.Today),
            TaxDate: DateOnly.FromDateTime(sap.TaxDate ?? DateTime.Today),
            CustomerReferenceNumber: sap.NumAtCard,
            Lines: sap.DocumentLines.Select(line => new SalesOrderLineDto(
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

    public async Task<SalesOrderListResult> ListAsync(SalesOrderFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var clampedPageSize = pageSize switch { 50 => 50, 100 => 100, _ => 25 };
        var offset = Math.Max(0, page - 1) * clampedPageSize;

        var (whereClause, parameters) = BuildWhereClause(filter);

        var countSql = $"""SELECT COUNT(*) FROM "ORDR" o {whereClause}""";
        var totalRecords = (await _hana.QueryAsync<int>(countSql, parameters, ct)).FirstOrDefault();

        var listSql = $"""
            SELECT o."DocEntry", o."DocNum", o."CardCode" AS "CustomerCardCode",
                   o."CardName" AS "CustomerName", o."DocDate", o."DocTotal",
                   o."NumAtCard" AS "CustomerReferenceNumber", s."SlpName" AS "SalesEmployeeName",
                   CASE WHEN o."DocStatus" = 'O' THEN 'Abierto' ELSE 'Cerrado' END AS "Status"
            FROM "ORDR" o
            LEFT JOIN "OSLP" s ON s."SlpCode" = o."SlpCode"
            {whereClause}
            ORDER BY o."DocEntry" DESC
            LIMIT {clampedPageSize} OFFSET {offset}
            """;

        var items = await _hana.QueryAsync<SalesOrderSummaryDto>(listSql, parameters, ct);
        return new SalesOrderListResult(items, totalRecords);
    }

    /// <summary>
    /// Arma el WHERE dinámicamente según qué filtros vinieron -- todos opcionales, así
    /// que se reusa el mismo WHERE (y el mismo diccionario de parámetros) para el
    /// COUNT(*) y el SELECT paginado, nunca concatenando el valor del usuario, solo los
    /// nombres de los parámetros (que son fijos, no vienen del usuario).
    /// </summary>
    private static (string WhereClause, IReadOnlyDictionary<string, object?> Parameters) BuildWhereClause(SalesOrderFilter? filter)
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

        var whereClause = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
        return (whereClause, parameters);
    }
}
