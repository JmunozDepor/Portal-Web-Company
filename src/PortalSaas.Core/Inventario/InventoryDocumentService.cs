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

    public InventoryDocumentService(IHanaService hana, ISapConnectionProvider connectionProvider)
    {
        _hana = hana;
        _connectionProvider = connectionProvider;
    }

    public Task<bool> CanCreateAsync(InventoryDocumentType type, CancellationToken ct = default) =>
        Task.FromResult(InventoryDocumentTypeCatalog.Resolve(type).DefaultCanCreate);

    public async Task<int> CreateAsync(InventoryDocumentType type, string portalUsername, InventoryDocumentDto document, CancellationToken ct = default)
    {
        var entry = InventoryDocumentTypeCatalog.Resolve(type);

        var header = new SapInventoryDocumentHeader
        {
            DocDate = document.DocDate.ToDateTime(TimeOnly.MinValue),
            DocDueDate = document.DocDueDate.ToDateTime(TimeOnly.MinValue),
            Comments = document.Comments,
            FromWarehouse = document.FromWarehouseCode,
            ToWarehouse = document.ToWarehouseCode,
            U_PortalUser = portalUsername,
            // SAP exige el par de almacenes POR LÍNEA -- se copian acá los defaults del
            // encabezado, el formulario solo los pide una vez (ver InventoryDocumentDto).
            DocumentLines = document.Lines.Select(line => new SapInventoryDocumentLine
            {
                ItemCode = line.ItemCode,
                Quantity = line.Quantity,
                WarehouseCode = document.ToWarehouseCode,
                FromWarehouseCode = document.FromWarehouseCode,
            }).ToList(),
        };

        var session = await _connectionProvider.GetConnectionAsync(ct);
        var created = await session.PostAsync<SapInventoryDocumentHeader>(entry.Resource, header, ct)
            ?? throw new InvalidOperationException("Service Layer no devolvió el documento creado.");

        return created.DocEntry ?? throw new InvalidOperationException("El documento se creó sin DocEntry.");
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

        var firstLine = sap.DocumentLines.FirstOrDefault();

        return new InventoryDocumentDto(
            FromWarehouseCode: sap.FromWarehouse ?? firstLine?.FromWarehouseCode ?? string.Empty,
            ToWarehouseCode: sap.ToWarehouse ?? firstLine?.WarehouseCode ?? string.Empty,
            DocDate: DateOnly.FromDateTime(sap.DocDate ?? DateTime.Today),
            DocDueDate: DateOnly.FromDateTime(sap.DocDueDate ?? DateTime.Today),
            Comments: sap.Comments,
            Lines: sap.DocumentLines.Select(line => new InventoryDocumentLineDto(
                ItemCode: line.ItemCode,
                Description: null,
                Quantity: line.Quantity)).ToList(),
            DocEntry: sap.DocEntry,
            DocNum: sap.DocNum,
            Status: sap.DocumentStatus == "bost_Close" ? "Cerrado" : "Abierto");
    }

    public async Task<InventoryDocumentListResult> ListAsync(InventoryDocumentType type, InventoryDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var entry = InventoryDocumentTypeCatalog.Resolve(type);
        var clampedPageSize = pageSize switch { 50 => 50, 100 => 100, _ => 25 };
        var offset = Math.Max(0, page - 1) * clampedPageSize;

        var (whereClause, parameters) = BuildWhereClause(filter);

        var countSql = $"""SELECT COUNT(*) FROM "{entry.Table}" o {whereClause}""";
        var totalRecords = (await _hana.QueryAsync<int>(countSql, parameters, ct)).FirstOrDefault();

        // Sin almacén origen/destino acá a propósito -- ver el doc-comment de
        // InventoryDocumentSummaryDto.
        var listSql = $"""
            SELECT o."DocEntry", o."DocNum", o."DocDate",
                   CASE WHEN o."DocStatus" = 'O' THEN 'Abierto' ELSE 'Cerrado' END AS "Status"
            FROM "{entry.Table}" o
            {whereClause}
            ORDER BY o."DocEntry" DESC
            LIMIT {clampedPageSize} OFFSET {offset}
            """;

        var items = await _hana.QueryAsync<InventoryDocumentSummaryDto>(listSql, parameters, ct);
        return new InventoryDocumentListResult(items, totalRecords);
    }

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
                clauses.Add("""o."DocDate" <= :dateTo""");
                parameters["dateTo"] = dateTo.ToDateTime(TimeOnly.MinValue);
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
