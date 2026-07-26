using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Compras;

/// <summary>
/// Motor genérico de documentos de compra -- tabla HANA/recurso Service Layer
/// resueltos vía PurchaseDocumentTypeCatalog, portado de GenericoCompraService en
/// referencia-original/PortalSAP_v2. Copia deliberada de SalesDocumentService en la
/// forma (mismo criterio que el original: los 3 motores son implementaciones
/// paralelas, no una superclase compartida -- ver CLAUDE.md), sin la lógica de
/// filtros extra (ningún tipo de Compra comparte tabla/recurso acá).
/// </summary>
public sealed class PurchaseDocumentService : IPurchaseDocumentService
{
    private readonly IHanaService _hana;
    private readonly ISapConnectionProvider _connectionProvider;

    public PurchaseDocumentService(IHanaService hana, ISapConnectionProvider connectionProvider)
    {
        _hana = hana;
        _connectionProvider = connectionProvider;
    }

    public Task<bool> CanCreateAsync(PurchaseDocumentType type, CancellationToken ct = default) =>
        Task.FromResult(PurchaseDocumentTypeCatalog.Resolve(type).DefaultCanCreate);

    public async Task<int> CreateAsync(PurchaseDocumentType type, string portalUsername, PurchaseDocumentDto document, CancellationToken ct = default)
    {
        var entry = PurchaseDocumentTypeCatalog.Resolve(type);

        var header = new SapPurchaseDocumentHeader
        {
            CardCode = document.SupplierCardCode,
            CardName = document.SupplierName,
            Comments = document.Comments,
            DocDate = document.DocDate.ToDateTime(TimeOnly.MinValue),
            DocDueDate = document.DocDueDate.ToDateTime(TimeOnly.MinValue),
            NumAtCard = document.SupplierReferenceNumber,
            U_PortalUser = portalUsername,
            DocumentLines = document.Lines.Select(line => new SapPurchaseDocumentLine
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
        var created = await session.PostAsync<SapPurchaseDocumentHeader>(entry.Resource, header, ct)
            ?? throw new InvalidOperationException("Service Layer no devolvió el documento creado.");

        return created.DocEntry ?? throw new InvalidOperationException("El documento se creó sin DocEntry.");
    }

    public async Task<PurchaseDocumentDto?> GetAsync(PurchaseDocumentType type, int docEntry, CancellationToken ct = default)
    {
        var entry = PurchaseDocumentTypeCatalog.Resolve(type);
        var session = await _connectionProvider.GetConnectionAsync(ct);
        var sap = await session.GetAsync<SapPurchaseDocumentHeader>($"{entry.Resource}({docEntry})", ct: ct);
        if (sap is null)
        {
            return null;
        }

        return new PurchaseDocumentDto(
            SupplierCardCode: sap.CardCode,
            SupplierName: sap.CardName,
            Comments: sap.Comments,
            DocDate: DateOnly.FromDateTime(sap.DocDate ?? DateTime.Today),
            DocDueDate: DateOnly.FromDateTime(sap.DocDueDate ?? DateTime.Today),
            SupplierReferenceNumber: sap.NumAtCard,
            Lines: sap.DocumentLines.Select(line => new PurchaseDocumentLineDto(
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

    public async Task<PurchaseDocumentListResult> ListAsync(PurchaseDocumentType type, PurchaseDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var entry = PurchaseDocumentTypeCatalog.Resolve(type);
        var clampedPageSize = pageSize switch { 50 => 50, 100 => 100, _ => 25 };
        var offset = Math.Max(0, page - 1) * clampedPageSize;

        var (whereClause, parameters) = BuildWhereClause(filter);

        var countSql = $"""SELECT COUNT(*) FROM "{entry.Table}" o {whereClause}""";
        var totalRecords = (await _hana.QueryAsync<int>(countSql, parameters, ct)).FirstOrDefault();

        var listSql = $"""
            SELECT o."DocEntry", o."DocNum", o."CardCode" AS "SupplierCardCode",
                   o."CardName" AS "SupplierName", o."DocDate", o."DocTotal",
                   o."NumAtCard" AS "SupplierReferenceNumber",
                   CASE WHEN o."DocStatus" = 'O' THEN 'Abierto' ELSE 'Cerrado' END AS "Status"
            FROM "{entry.Table}" o
            {whereClause}
            ORDER BY o."DocEntry" DESC
            LIMIT {clampedPageSize} OFFSET {offset}
            """;

        var items = await _hana.QueryAsync<PurchaseDocumentSummaryDto>(listSql, parameters, ct);
        return new PurchaseDocumentListResult(items, totalRecords);
    }

    private static (string WhereClause, IReadOnlyDictionary<string, object?> Parameters) BuildWhereClause(PurchaseDocumentFilter? filter)
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

            if (!string.IsNullOrWhiteSpace(filter.SupplierCardCode))
            {
                clauses.Add("""o."CardCode" = :supplierCardCode""");
                parameters["supplierCardCode"] = filter.SupplierCardCode.Trim();
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
