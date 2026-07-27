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
    private readonly IOrganizationDocumentPermissionService _permissions;

    public SalesDocumentService(IHanaService hana, ISapConnectionProvider connectionProvider, IOrganizationDocumentPermissionService permissions)
    {
        _hana = hana;
        _connectionProvider = connectionProvider;
        _permissions = permissions;
    }

    public int GetSapObjectCode(SalesDocumentType type) => SalesDocumentTypeCatalog.Resolve(type).ObjectCode;

    public Task<bool> CanCreateAsync(SalesDocumentType type, CancellationToken ct = default) =>
        _permissions.IsCreateAllowedAsync("Sales", type.ToString(), SalesDocumentTypeCatalog.Resolve(type).DefaultCanCreate, ct);

    public async Task<int> CreateAsync(SalesDocumentType type, string portalUsername, SalesDocumentDto document, CancellationToken ct = default)
    {
        var entry = SalesDocumentTypeCatalog.Resolve(type);

        var header = new SapSalesDocumentHeader
        {
            DocType = ResolveDocType(document.Lines),
            CardCode = document.CustomerCardCode,
            CardName = document.CustomerName,
            SalesPersonCode = document.SalesEmployeeCode,
            Series = document.Series,
            TransportationCode = document.ShippingMethodCode,
            GroupNumber = document.PaymentTermsGroupCode,
            Comments = document.Comments,
            DocDate = document.DocDate.ToDateTime(TimeOnly.MinValue),
            DocDueDate = document.DocDueDate.ToDateTime(TimeOnly.MinValue),
            TaxDate = document.TaxDate.ToDateTime(TimeOnly.MinValue),
            NumAtCard = document.CustomerReferenceNumber,
            U_PortalUser = portalUsername,
            AdditionalFields = document.AdditionalFields,
            DocumentLines = document.Lines.Select(line => new SapSalesDocumentLine
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
                AdditionalFields = line.AdditionalFields,
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
                // ItemType no es confiable en documentos preexistentes (a veces vuelve
                // vacío) -- ItemCode poblado es la señal fuerte de línea de Artículo,
                // mismo criterio que la referencia.
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
            Series: sap.Series,
            ShippingMethodCode: sap.TransportationCode,
            PaymentTermsGroupCode: sap.GroupNumber);
    }

    public async Task<SalesDocumentListResult> ListAsync(SalesDocumentType type, SalesDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var entry = SalesDocumentTypeCatalog.Resolve(type);
        var clampedPageSize = pageSize switch { 50 => 50, 100 => 100, _ => 25 };
        var offset = Math.Max(0, page - 1) * clampedPageSize;

        var (whereClause, parameters) = BuildWhereClause(filter, entry.ExtraFilters);

        // El filtro de Vendedor (s."SlpName") exige el mismo LEFT JOIN "OSLP" en el
        // COUNT que en el SELECT -- sin esto, "invalid column" apenas se usa ese filtro
        // (el alias "s" no existiría en esa consulta).
        var countSql = $"""
            SELECT COUNT(*) FROM "{entry.Table}" o
            LEFT JOIN "OSLP" s ON s."SlpCode" = o."SlpCode"
            {whereClause}
            """;
        var totalRecords = (await _hana.QueryAsync<int>(countSql, parameters, ct)).FirstOrDefault();

        var listSql = $"""
            SELECT o."DocEntry", o."DocNum", o."CardCode" AS "CustomerCardCode",
                   o."CardName" AS "CustomerName", o."Address2" AS "DeliveryAddress",
                   o."DocDate", o."DocTotal",
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
        var hasAdditionalFields = header.AdditionalFields is not null || header.DocumentLines.Any(l => l.AdditionalFields is not null);
        if (equalFilters.Count == 0 && !hasAdditionalFields)
        {
            return header;
        }

        // SapAdditionalFieldsHelper.Flatten ya aplana AdditionalFields (cabecera + cada
        // línea) -- se reusa acá en vez de un JSON round-trip aparte, para no tener 2
        // mecanismos de aplanado distintos conviviendo en el mismo servicio.
        var flattened = PortalSaas.Core.Sap.SapAdditionalFieldsHelper.Flatten(header);
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
    /// IsEqual -- portado de GenericoVentaService.ListarAsync, MISMO comportamiento de
    /// filtro (no una aproximación): todo texto es LIKE parcial case-insensitive
    /// (UPPER(...) LIKE UPPER(:x), incluido DocNum -- buscar "123" encuentra "51230"),
    /// FechaHasta es límite EXCLUSIVO del día siguiente (así incluye todo el día
    /// "hasta", no solo su medianoche 00:00:00).
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
                clauses.Add("""o."DocDate" < :dateTo""");
                parameters["dateTo"] = dateTo.ToDateTime(TimeOnly.MinValue).AddDays(1);
            }

            if (!string.IsNullOrWhiteSpace(filter.CustomerCardCode))
            {
                clauses.Add("""UPPER(o."CardCode") LIKE UPPER(:customerCardCode)""");
                parameters["customerCardCode"] = $"%{filter.CustomerCardCode.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.CustomerName))
            {
                clauses.Add("""UPPER(o."CardName") LIKE UPPER(:customerName)""");
                parameters["customerName"] = $"%{filter.CustomerName.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.CustomerReferenceNumber))
            {
                clauses.Add("""UPPER(o."NumAtCard") LIKE UPPER(:customerReferenceNumber)""");
                parameters["customerReferenceNumber"] = $"%{filter.CustomerReferenceNumber.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.SalesEmployeeName))
            {
                clauses.Add("""UPPER(s."SlpName") LIKE UPPER(:salesEmployeeName)""");
                parameters["salesEmployeeName"] = $"%{filter.SalesEmployeeName.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.DocNum))
            {
                clauses.Add("""TO_VARCHAR(o."DocNum") LIKE :docNum""");
                parameters["docNum"] = $"%{filter.DocNum.Trim()}%";
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

    /// <summary>
    /// DocType es del documento entero, no por línea -- SAP no permite mezclar
    /// Artículo/Servicio en el mismo documento, así que alcanza con mirar la primera
    /// línea (el portal siempre digita documentos homogéneos, ver DocumentLineType).
    /// </summary>
    private static string ResolveDocType(IReadOnlyList<SalesDocumentLineDto> lines) =>
        lines.Count > 0 && lines[0].Type == DocumentLineType.Service
            ? "dDocument_Service"
            : "dDocument_Items";
}
