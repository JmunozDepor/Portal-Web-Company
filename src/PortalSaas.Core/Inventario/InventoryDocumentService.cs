using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Inventario;

/// <summary>
/// Motor genérico de documentos de inventario -- traslado de mercadería entre 2
/// almacenes, sin cliente/proveedor. Tabla HANA/recurso Service Layer resueltos vía
/// InventoryDocumentTypeCatalog, portado de GenericoInventarioService en
/// referencia-original/PortalSAP_v2. Copia deliberada de SalesDocumentService en la
/// forma (mismo criterio que el original: los 3 motores son implementaciones
/// paralelas, no una superclase compartida -- ver CLAUDE.md).
/// </summary>
public sealed class InventoryDocumentService : IInventoryDocumentService
{
    private readonly IHanaService _hana;
    private readonly ISapConnectionProvider _connectionProvider;
    private readonly IOrganizationDocumentPermissionService _permissions;

    public InventoryDocumentService(IHanaService hana, ISapConnectionProvider connectionProvider, IOrganizationDocumentPermissionService permissions)
    {
        _hana = hana;
        _connectionProvider = connectionProvider;
        _permissions = permissions;
    }

    public int GetSapObjectCode(InventoryDocumentType type) => InventoryDocumentTypeCatalog.Resolve(type).ObjectCode;

    public Task<bool> CanCreateAsync(InventoryDocumentType type, CancellationToken ct = default) =>
        _permissions.IsCreateAllowedAsync("Inventory", type.ToString(), InventoryDocumentTypeCatalog.Resolve(type).DefaultCanCreate, ct);

    public async Task<int> CreateAsync(InventoryDocumentType type, string portalUsername, InventoryDocumentDto document, CancellationToken ct = default)
    {
        var entry = InventoryDocumentTypeCatalog.Resolve(type);

        var header = new SapInventoryDocumentHeader
        {
            DocDate = document.DocDate.ToDateTime(TimeOnly.MinValue),
            Series = document.Series,
            Comments = document.Comments,
            CardCode = document.BusinessPartnerCardCode,
            NumAtCard = document.CustomerReferenceNumber,
            U_PortalUser = portalUsername,
            AdditionalFields = document.AdditionalFields,
            StockTransferLines = document.Lines.Select(MapLine).ToList(),
        };

        var hasAdditionalFields = header.AdditionalFields is not null || header.StockTransferLines.Any(l => l.AdditionalFields is not null);
        object requestBody = hasAdditionalFields ? PortalSaas.Core.Sap.SapAdditionalFieldsHelper.Flatten(header) : header;

        var session = await _connectionProvider.GetConnectionAsync(ct);
        var created = await session.PostAsync<SapInventoryDocumentHeader>(entry.Resource, requestBody, ct)
            ?? throw new InvalidOperationException("Service Layer no devolvió el documento creado.");

        return created.DocEntry ?? throw new InvalidOperationException("El documento se creó sin DocEntry.");
    }

    public async Task AddLinesAsync(InventoryDocumentType type, int docEntry, IReadOnlyList<InventoryDocumentLineDto> newLines, CancellationToken ct = default)
    {
        var entry = InventoryDocumentTypeCatalog.Resolve(type);
        var session = await _connectionProvider.GetConnectionAsync(ct);

        var current = await session.GetAsync<SapInventoryDocumentHeader>($"{entry.Resource}({docEntry})", ct: ct)
            ?? throw new InvalidOperationException($"No se pudo releer el documento {docEntry} para agregar líneas.");

        var combinedLines = current.StockTransferLines.Select(PortalSaas.Core.Sap.SapAdditionalFieldsHelper.Flatten)
            .Concat(newLines.Select(MapLine).Select(PortalSaas.Core.Sap.SapAdditionalFieldsHelper.Flatten))
            .ToList();

        await session.PatchAsync(entry.Resource, docEntry, new { StockTransferLines = combinedLines }, ct);
    }

    public async Task<InventoryDocumentDto?> GetAsync(InventoryDocumentType type, int docEntry, CancellationToken ct = default)
    {
        var entry = InventoryDocumentTypeCatalog.Resolve(type);
        var session = await _connectionProvider.GetConnectionAsync(ct);
        var sap = await session.GetAsync<SapInventoryDocumentHeader>($"{entry.Resource}({docEntry})", ct: ct);
        if (sap is null)
        {
            return null;
        }

        return new InventoryDocumentDto(
            DocDate: DateOnly.FromDateTime(sap.DocDate ?? DateTime.Today),
            Comments: sap.Comments,
            Lines: sap.StockTransferLines.Select(line => new InventoryDocumentLineDto(
                ItemCode: line.ItemCode,
                Description: null,
                Quantity: line.Quantity,
                FromWarehouseCode: line.FromWarehouseCode,
                ToWarehouseCode: line.WarehouseCode)).ToList(),
            DocEntry: sap.DocEntry,
            DocNum: sap.DocNum,
            Status: sap.DocumentStatus == "bost_Close" ? "Cerrado" : "Abierto",
            Series: sap.Series,
            BusinessPartnerCardCode: sap.CardCode,
            BusinessPartnerName: sap.CardName,
            CustomerReferenceNumber: sap.NumAtCard);
    }

    public async Task<InventoryDocumentListResult> ListAsync(InventoryDocumentType type, InventoryDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var entry = InventoryDocumentTypeCatalog.Resolve(type);
        var clampedPageSize = pageSize switch { 50 => 50, 100 => 100, _ => 25 };
        var offset = Math.Max(0, page - 1) * clampedPageSize;

        var (whereClause, parameters) = BuildWhereClause(filter);

        var countSql = $"""SELECT COUNT(*) FROM "{entry.Table}" o {whereClause}""";
        var totalRecords = (await _hana.QueryAsync<int>(countSql, parameters, ct)).FirstOrDefault();

        // Paridad con GenericoInventarioService.ListarAsync del original -- socio de
        // negocios/nombre/dirección de destino/almacén destino de cabecera/N.° ref.
        // (ver el doc-comment de InventoryDocumentSummaryDto). Sin almacén origen/
        // destino DE LÍNEA acá a propósito -- eso sigue sin equivalente en el listado.
        var listSql = $"""
            SELECT o."DocEntry", o."DocNum", o."CardCode" AS "BusinessPartnerCardCode",
                   o."CardName" AS "BusinessPartnerName", o."DocDate",
                   o."Address2" AS "DeliveryAddress", o."U_NumAtCard" AS "CustomerReferenceNumber",
                   TO_VARCHAR(o."ToWhsCode") AS "WarehouseDestinationCode",
                   CASE WHEN o."DocStatus" = 'O' THEN 'Abierto' ELSE 'Cerrado' END AS "Status"
            FROM "{entry.Table}" o
            {whereClause}
            ORDER BY o."DocEntry" DESC
            LIMIT {clampedPageSize} OFFSET {offset}
            """;

        var items = await _hana.QueryAsync<InventoryDocumentSummaryDto>(listSql, parameters, ct);
        return new InventoryDocumentListResult(items, totalRecords);
    }

    /// <summary>
    /// Mismo criterio de filtro que SalesDocumentService/PurchaseDocumentService.
    /// BuildWhereClause (regla de paridad entre motores, ver CLAUDE.md): DocNum es LIKE
    /// parcial (buscar "123" encuentra "51230"), DateTo es límite EXCLUSIVO del día
    /// siguiente. CardCode/CardName/ToWhsCode -- paridad con FiltroGenericoInventario del
    /// original, mismo LIKE parcial case-insensitive que los otros dos motores.
    /// </summary>
    private static (string WhereClause, IReadOnlyDictionary<string, object?> Parameters) BuildWhereClause(InventoryDocumentFilter? filter)
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

            if (!string.IsNullOrWhiteSpace(filter.DocNum))
            {
                clauses.Add("""TO_VARCHAR(o."DocNum") LIKE :docNum""");
                parameters["docNum"] = $"%{filter.DocNum.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.BusinessPartnerCardCode))
            {
                clauses.Add("""UPPER(o."CardCode") LIKE UPPER(:cardCode)""");
                parameters["cardCode"] = $"%{filter.BusinessPartnerCardCode.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.BusinessPartnerName))
            {
                clauses.Add("""UPPER(o."CardName") LIKE UPPER(:cardName)""");
                parameters["cardName"] = $"%{filter.BusinessPartnerName.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.WarehouseDestinationCode))
            {
                clauses.Add("""UPPER(o."ToWhsCode") LIKE UPPER(:warehouseDestination)""");
                parameters["warehouseDestination"] = $"%{filter.WarehouseDestinationCode.Trim()}%";
            }
        }

        var whereClause = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
        return (whereClause, parameters);
    }

    private static SapInventoryDocumentLine MapLine(InventoryDocumentLineDto line) => new()
    {
        ItemCode = line.ItemCode,
        Quantity = line.Quantity,
        WarehouseCode = line.ToWarehouseCode,
        FromWarehouseCode = line.FromWarehouseCode,
        AdditionalFields = line.AdditionalFields,
    };
}
