using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Sap;

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
    private readonly IOrganizationDocumentPermissionService _permissions;
    private readonly ISapTraceabilityFieldResolver _traceability;

    public PurchaseDocumentService(IHanaService hana, ISapConnectionProvider connectionProvider,
        IOrganizationDocumentPermissionService permissions, ISapTraceabilityFieldResolver traceability)
    {
        _hana = hana;
        _connectionProvider = connectionProvider;
        _permissions = permissions;
        _traceability = traceability;
    }

    public int GetSapObjectCode(PurchaseDocumentType type) => PurchaseDocumentTypeCatalog.Resolve(type).ObjectCode;

    public bool SupportsCancel(PurchaseDocumentType type) => PurchaseDocumentTypeCatalog.Resolve(type).SupportsCancel;

    public Task<bool> CanCreateAsync(PurchaseDocumentType type, CancellationToken ct = default) =>
        _permissions.IsCreateAllowedAsync("Purchase", type.ToString(), PurchaseDocumentTypeCatalog.Resolve(type).DefaultCanCreate, ct);

    public async Task<int> CreateAsync(PurchaseDocumentType type, string portalUsername, PurchaseDocumentDto document, CancellationToken ct = default)
    {
        var entry = PurchaseDocumentTypeCatalog.Resolve(type);

        var requiredDate = document.DocDueDate.ToDateTime(TimeOnly.MinValue);

        var udfName = await _traceability.ResolveUserFieldNameAsync(ct);
        var isDefaultUdf = string.Equals(udfName, SapTraceabilityFieldResolver.DefaultUserFieldName, StringComparison.OrdinalIgnoreCase);

        var header = new SapPurchaseDocumentHeader
        {
            DocType = ResolveDocType(document.Lines),
            CardCode = document.SupplierCardCode,
            CardName = document.SupplierName,
            Series = document.Series,
            Comments = document.Comments,
            DocDate = document.DocDate.ToDateTime(TimeOnly.MinValue),
            DocDueDate = requiredDate,
            RequriedDate = requiredDate,
            TaxDate = document.TaxDate.ToDateTime(TimeOnly.MinValue),
            NumAtCard = document.SupplierReferenceNumber,
            U_PortalUser = isDefaultUdf ? portalUsername : null,
            AdditionalFields = isDefaultUdf
                ? document.AdditionalFields
                : SapAdditionalFieldsHelper.MergeTraceabilityUser(document.AdditionalFields, udfName, portalUsername),
            DocumentLines = document.Lines.Select(line => MapLine(line, requiredDate)).ToList(),
        };

        var hasAdditionalFields = header.AdditionalFields is not null || header.DocumentLines.Any(l => l.AdditionalFields is not null);
        object requestBody = hasAdditionalFields ? PortalSaas.Core.Sap.SapAdditionalFieldsHelper.Flatten(header) : header;

        var session = await _connectionProvider.GetConnectionAsync(ct);
        var created = await session.PostAsync<SapPurchaseDocumentHeader>(entry.Resource, requestBody, ct)
            ?? throw new InvalidOperationException("Service Layer no devolvió el documento creado.");

        return created.DocEntry ?? throw new InvalidOperationException("El documento se creó sin DocEntry.");
    }

    public async Task AddLinesAsync(PurchaseDocumentType type, int docEntry, IReadOnlyList<PurchaseDocumentLineDto> newLines, CancellationToken ct = default)
    {
        var entry = PurchaseDocumentTypeCatalog.Resolve(type);
        var session = await _connectionProvider.GetConnectionAsync(ct);

        var current = await session.GetAsync<SapPurchaseDocumentHeader>($"{entry.Resource}({docEntry})", ct: ct)
            ?? throw new InvalidOperationException($"No se pudo releer el documento {docEntry} para agregar líneas.");

        // RequiredDate por línea se postea igual al DocDueDate ya grabado en el documento
        // (mismo criterio que CreateAsync -- ver comentario de SapPurchaseDocumentHeader).
        var requiredDate = current.DocDueDate;

        var combinedLines = current.DocumentLines.Select(PortalSaas.Core.Sap.SapAdditionalFieldsHelper.Flatten)
            .Concat(newLines.Select(line => MapLine(line, requiredDate)).Select(PortalSaas.Core.Sap.SapAdditionalFieldsHelper.Flatten))
            .ToList();

        await session.PatchAsync(entry.Resource, docEntry, new { DocumentLines = combinedLines }, ct);
    }

    public async Task CloseAsync(PurchaseDocumentType type, int docEntry, CancellationToken ct = default)
    {
        var entry = PurchaseDocumentTypeCatalog.Resolve(type);
        var session = await _connectionProvider.GetConnectionAsync(ct);
        await session.PostAsync($"{entry.Resource}({docEntry})/Close", new { }, ct);
    }

    public async Task CancelAsync(PurchaseDocumentType type, int docEntry, CancellationToken ct = default)
    {
        var entry = PurchaseDocumentTypeCatalog.Resolve(type);
        var session = await _connectionProvider.GetConnectionAsync(ct);
        await session.PostAsync($"{entry.Resource}({docEntry})/Cancel", new { }, ct);
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
            TaxDate: DateOnly.FromDateTime(sap.TaxDate ?? DateTime.Today),
            SupplierReferenceNumber: sap.NumAtCard,
            Lines: sap.DocumentLines.Select(line => new PurchaseDocumentLineDto(
                // ItemType no es confiable en documentos preexistentes -- ItemCode
                // poblado es la señal fuerte de línea de Artículo, mismo criterio que
                // SalesDocumentService.GetAsync.
                Type: !string.IsNullOrWhiteSpace(line.ItemCode) ? DocumentLineType.Item : DocumentLineType.Service,
                ItemCode: line.ItemCode,
                Description: line.ItemDescription,
                Quantity: line.Quantity,
                UnitPrice: line.UnitPrice,
                DiscountPercent: line.DiscountPercent,
                WarehouseCode: line.WarehouseCode,
                AccountCode: line.AccountCode,
                CostCenterCode: line.CostingCode,
                CostCenterCode2: line.CostingCode2,
                CostCenterCode3: line.CostingCode3)).ToList(),
            DocEntry: sap.DocEntry,
            DocNum: sap.DocNum,
            DocTotal: sap.DocTotal,
            Status: sap.DocumentStatus == "bost_Close" ? "Cerrado" : "Abierto",
            Series: sap.Series);
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

    /// <summary>
    /// Mismo criterio de filtro que SalesDocumentService.BuildWhereClause (regla de
    /// paridad entre motores, ver CLAUDE.md -- bug real encontrado ahí primero, portado
    /// acá): todo texto es LIKE parcial case-insensitive (incluido DocNum -- buscar "123"
    /// encuentra "51230"), DateTo es límite EXCLUSIVO del día siguiente (así incluye todo
    /// el día "hasta", no solo su medianoche 00:00:00). Sin filtro de "Vendedor"/
    /// "Sucursal entrega" acá -- el original (`FiltroGenericoCompra`) tampoco los tiene
    /// para Compra, solo Venta/Inventario.
    /// </summary>
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
                clauses.Add("""o."DocDate" < :dateTo""");
                parameters["dateTo"] = dateTo.ToDateTime(TimeOnly.MinValue).AddDays(1);
            }

            if (!string.IsNullOrWhiteSpace(filter.SupplierCardCode))
            {
                clauses.Add("""UPPER(o."CardCode") LIKE UPPER(:supplierCardCode)""");
                parameters["supplierCardCode"] = $"%{filter.SupplierCardCode.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.SupplierName))
            {
                clauses.Add("""UPPER(o."CardName") LIKE UPPER(:supplierName)""");
                parameters["supplierName"] = $"%{filter.SupplierName.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.SupplierReferenceNumber))
            {
                clauses.Add("""UPPER(o."NumAtCard") LIKE UPPER(:supplierReferenceNumber)""");
                parameters["supplierReferenceNumber"] = $"%{filter.SupplierReferenceNumber.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.DocNum))
            {
                clauses.Add("""TO_VARCHAR(o."DocNum") LIKE :docNum""");
                parameters["docNum"] = $"%{filter.DocNum.Trim()}%";
            }
        }

        var whereClause = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
        return (whereClause, parameters);
    }

    /// <summary>DocType es del documento entero, no por línea -- ver el doc-comment equivalente en SalesDocumentService.</summary>
    private static string ResolveDocType(IReadOnlyList<PurchaseDocumentLineDto> lines) =>
        lines.Count > 0 && lines[0].Type == DocumentLineType.Service
            ? "dDocument_Service"
            : "dDocument_Items";

    private static SapPurchaseDocumentLine MapLine(PurchaseDocumentLineDto line, DateTime? requiredDate) => new()
    {
        ItemType = line.Type == DocumentLineType.Service ? "itService" : "itItems",
        ItemCode = line.Type == DocumentLineType.Item ? line.ItemCode : null,
        ItemDescription = line.Description,
        Quantity = line.Quantity,
        UnitPrice = line.UnitPrice,
        DiscountPercent = line.DiscountPercent,
        WarehouseCode = line.Type == DocumentLineType.Item ? line.WarehouseCode : null,
        AccountCode = line.Type == DocumentLineType.Service ? line.AccountCode : null,
        CostingCode = line.Type == DocumentLineType.Service ? line.CostCenterCode : null,
        CostingCode2 = line.Type == DocumentLineType.Service ? line.CostCenterCode2 : null,
        CostingCode3 = line.Type == DocumentLineType.Service ? line.CostCenterCode3 : null,
        RequiredDate = requiredDate,
        BaseType = line.BaseType,
        BaseEntry = line.BaseEntry,
        BaseLine = line.BaseLine,
        AdditionalFields = line.AdditionalFields,
    };
}
