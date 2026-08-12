using System.Data;
using System.Globalization;
using ClosedXML.Excel;
using ExcelDataReader;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// Motor de IGenericImportService -- ver el contrato para el porqué de no postear
/// nunca directo a Service Layer. Portado de ImportacionGenericaService
/// (referencia-original/PortalSAP_v2) -- misma lógica de resolución de filas/
/// agrupación/plantilla, adaptado a los 3 motores genéricos de este proyecto
/// (SalesDocumentService/PurchaseDocumentService/InventoryDocumentService) y a la
/// configuración propia (organization-scoped, ver IGenericImportConfigService).
///
/// DimCode de Centro de Costos/Dimensión2/Dimensión3 -- mismos valores ya confirmados
/// empíricamente y usados por los formularios manuales de Venta/Compra (ver
/// CostCenterCatalogService/CLAUDE.md, 26 jul 2026): 1/2/5 para los dos módulos --
/// a diferencia del original (Dimensión3 = 3 en Compra), acá no se reintroduce un
/// valor propio de Compra sin confirmar contra este ambiente.
/// </summary>
public sealed class GenericImportService : IGenericImportService
{
    private const int DimCodeCostCenter = 1;
    private const int DimCodeDimension2 = 2;
    private const int DimCodeDimension3 = 5;

    /// <summary>Clave de agrupamiento cuando la configuración no define GroupingColumn -- todo el archivo cae en un único documento.</summary>
    private const string SingleGroupingKey = "__SINGLE__";

    // Tamaño de lote para la carga de líneas -- un documento con más filas que esto se
    // crea con el primer lote y el resto se agrega en lotes sucesivos (ver
    // I{Sales,Purchase,Inventory}DocumentService.AddLinesAsync), en vez de mandar todas
    // las líneas en un único POST a Service Layer. Sin esto, un archivo de varios miles
    // de filas sin GroupingColumn configurado (o con un grupo muy grande) arma un único
    // documento gigante que puede superar el timeout de Service Layer y además deja al
    // usuario sin ningún indicio de progreso durante todo el proceso (barra fija en
    // "0 de 1"). Portado de ImportacionGenericaService.TamanoLotePorDefecto
    // (referencia-original/PortalSAP_v2) -- ese mismo valor (400, no 200) es el
    // resultado de un ajuste real: con lotes de 200 un archivo de 1600 filas pareció
    // "colgado" y el usuario cerró la pestaña a mitad de camino (2026-07-24). Cada lote
    // reprocesa en SAP el documento COMPLETO acumulado hasta ese momento
    // (AddLinesAsync relee y repatchea el arreglo entero) -- MENOS lotes (más grandes)
    // significa MENOS pasadas de reprocesamiento acumuladas para el mismo archivo.
    private const int DefaultBatchSize = 400;

    private static readonly IReadOnlyList<IGenericImportValidationRule> BuiltInRules =
    [
        new PositiveQuantityRule(),
        new ValidDiscountPercentRule(),
    ];

    private readonly IGenericImportConfigService _config;
    private readonly IGenericImportUserFieldService _userFieldsCatalog;
    private readonly IBusinessPartnerDefaultsService _partnerDefaults;
    private readonly IItemCrossReferenceService _crossReference;
    private readonly IItemCatalogService _items;
    private readonly IWarehouseCatalogService _warehouses;
    private readonly IGeneralLedgerAccountCatalogService _accounts;
    private readonly ICostCenterCatalogService _costCenters;
    private readonly IPriceListService _priceList;
    private readonly ICustomerCatalogService _customers;
    private readonly ISupplierCatalogService _suppliers;
    private readonly ISalesDocumentService _sales;
    private readonly IPurchaseDocumentService _purchase;
    private readonly IInventoryDocumentService _inventory;
    private readonly IGenericImportProgressStore _progress;
    private readonly ILogger<GenericImportService> _logger;

    public GenericImportService(IGenericImportConfigService config, IGenericImportUserFieldService userFieldsCatalog,
        IBusinessPartnerDefaultsService partnerDefaults, IItemCrossReferenceService crossReference, IItemCatalogService items,
        IWarehouseCatalogService warehouses, IGeneralLedgerAccountCatalogService accounts, ICostCenterCatalogService costCenters,
        IPriceListService priceList, ICustomerCatalogService customers, ISupplierCatalogService suppliers,
        ISalesDocumentService sales, IPurchaseDocumentService purchase, IInventoryDocumentService inventory,
        IGenericImportProgressStore progress, ILogger<GenericImportService> logger)
    {
        _config = config;
        _userFieldsCatalog = userFieldsCatalog;
        _partnerDefaults = partnerDefaults;
        _crossReference = crossReference;
        _items = items;
        _warehouses = warehouses;
        _accounts = accounts;
        _costCenters = costCenters;
        _priceList = priceList;
        _customers = customers;
        _suppliers = suppliers;
        _sales = sales;
        _purchase = purchase;
        _inventory = inventory;
        _progress = progress;
        _logger = logger;
    }

    public async Task<GenericImportResultDto> ProcessFileAsync(GenericImportParametersDto parameters, Stream file, CancellationToken ct = default)
    {
        var config = await _config.ResolveAsync(parameters.Module, parameters.DocumentType, parameters.LineType,
            parameters.BusinessPartnerCardCode, ct);
        if (config is null)
        {
            return new GenericImportResultDto
            {
                HasValidConfig = false,
                ErrorMessage = "No hay una configuración de importación (ni del socio de negocio ni estándar de la organización) " +
                    "para este tipo de documento -- creá una en Configuración de Importación Genérica.",
            };
        }

        var table = ReadExcel(file);
        if (table.Rows.Count == 0)
        {
            return new GenericImportResultDto { HasValidConfig = false, ErrorMessage = "El archivo no tiene filas de datos." };
        }

        var userFieldsCatalog = (await _userFieldsCatalog.ListAsync(parameters.Module, ct))
            .Where(f => f.IsActive)
            .ToDictionary(f => f.Id);

        var rawRows = ReadRawRows(table, config);

        IReadOnlyList<string> CodesOf(GenericImportLogicalField field) => rawRows
            .Select(r => r.CoreFields.GetValueOrDefault(field))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Carga multi-socio (config.BusinessPartnerFromFile): todos los CardCode
        // distintos de la columna mapeada a BusinessPartnerCardCode. Si no, la lista es
        // el único socio del wizard (modo de siempre) -- mismo helper CodesOf que ya
        // usan Bodega/CuentaMayor/etc, resuelto UNA vez por conjunto de códigos
        // distintos, nunca por fila.
        var businessPartnersInFile = config.BusinessPartnerFromFile
            ? CodesOf(GenericImportLogicalField.BusinessPartnerCardCode)
            : (!string.IsNullOrWhiteSpace(parameters.BusinessPartnerCardCode) ? [parameters.BusinessPartnerCardCode] : Array.Empty<string>());

        // Solo en modo multi-socio: valida existencia contra el catálogo de Cliente
        // (Venta/Inventario) o Proveedor (Compra) y trae el nombre para la vista previa
        // -- mismo catálogo que ya reusa el selector del wizard (ver
        // OnGetSearchBusinessPartnersAsync en Pages/Importar/Index.cshtml.cs).
        var validBusinessPartners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (config.BusinessPartnerFromFile)
        {
            foreach (var cardCode in businessPartnersInFile)
            {
                var name = parameters.Module == GenericImportModule.Purchase
                    ? (await _suppliers.GetAsync(cardCode, ct))?.CardName
                    : (await _customers.GetAsync(cardCode, ct))?.CardName;
                if (name is not null)
                {
                    validBusinessPartners[cardCode] = name;
                }
            }
        }

        // Paridad SKU-socio -> ItemCode, solo si la configuración lo pide -- una llamada
        // POR CADA socio distinto de businessPartnersInFile (antes: una sola llamada con
        // el socio del wizard). Con un único socio (modo normal) esto sigue siendo
        // exactamente 1 llamada, igual que antes.
        var crossReferenceBySocio = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (config.SkuIsCustomerOwn)
        {
            foreach (var cardCode in businessPartnersInFile)
            {
                crossReferenceBySocio[cardCode] = (await _crossReference.ListAsync(cardCode, ct))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
                    .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().ItemCode, StringComparer.OrdinalIgnoreCase);
            }
        }

        var excelItemCodes = CodesOf(GenericImportLogicalField.ItemCode);
        // Unión de todos los ItemCode de todas las paridades resueltas -- trae de más si
        // hay SKUs sin paridad en algún socio, pero evita una segunda pasada.
        var itemCodesToResolve = config.SkuIsCustomerOwn
            ? crossReferenceBySocio.Values.SelectMany(d => excelItemCodes.Where(d.ContainsKey).Select(sku => d[sku]))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : excelItemCodes;

        var items = itemCodesToResolve.Count > 0
            ? (await _items.GetByCodesAsync(itemCodesToResolve, ct)).ToDictionary(i => i.ItemCode, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ItemDto>(StringComparer.OrdinalIgnoreCase);

        // Almacén (Venta/Compra) y SourceWarehouse/DestinationWarehouse (Inventario)
        // resuelven contra el mismo catálogo -- se combinan en una única consulta.
        var warehouseCodesNeeded = CodesOf(GenericImportLogicalField.Warehouse)
            .Concat(CodesOf(GenericImportLogicalField.SourceWarehouse))
            .Concat(CodesOf(GenericImportLogicalField.DestinationWarehouse))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var warehouses = warehouseCodesNeeded.Count > 0
            ? (await _warehouses.ListAsync(ct: ct)).Where(w => warehouseCodesNeeded.Contains(w.WarehouseCode)).ToDictionary(w => w.WarehouseCode, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, WarehouseDto>(StringComparer.OrdinalIgnoreCase);

        var accountCodesNeeded = CodesOf(GenericImportLogicalField.Account).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accounts = accountCodesNeeded.Count > 0
            ? (await _accounts.ListAsync(ct: ct)).Where(a => accountCodesNeeded.Contains(a.AccountCode)).ToDictionary(a => a.AccountCode, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, GeneralLedgerAccountDto>(StringComparer.OrdinalIgnoreCase);

        var costCenterCodesNeeded = CodesOf(GenericImportLogicalField.CostCenter).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var costCenters = costCenterCodesNeeded.Count > 0
            ? (await _costCenters.ListAsync(ct: ct)).Where(c => costCenterCodesNeeded.Contains(c.Code)).ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CostCenterDto>(StringComparer.OrdinalIgnoreCase);

        var dimension2CodesNeeded = CodesOf(GenericImportLogicalField.Dimension2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dimension2 = dimension2CodesNeeded.Count > 0
            ? (await _costCenters.ListByDimensionAsync(DimCodeDimension2, ct: ct)).Where(c => dimension2CodesNeeded.Contains(c.Code)).ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CostCenterDto>(StringComparer.OrdinalIgnoreCase);

        var dimension3CodesNeeded = CodesOf(GenericImportLogicalField.Dimension3).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dimension3 = dimension3CodesNeeded.Count > 0
            ? (await _costCenters.ListByDimensionAsync(DimCodeDimension3, ct: ct)).Where(c => dimension3CodesNeeded.Contains(c.Code)).ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CostCenterDto>(StringComparer.OrdinalIgnoreCase);

        // Precio de sistema (GenericImportConfigDto.PriceSource == System): se resuelve
        // una sola vez para todos los artículos distintos del archivo. Solo aplica a
        // líneas de Artículo con precio (Servicio no tiene ItemCode, Inventario no
        // tiene UnitPrice en absoluto).
        IReadOnlyDictionary<string, decimal> systemPrices = new Dictionary<string, decimal>();
        if (config.PriceSource == GenericImportPriceSource.System && config.SystemPriceListCode is { } systemPriceList
            && parameters.Module != GenericImportModule.Inventory && parameters.LineType == GenericImportLineType.Item
            && items.Count > 0)
        {
            systemPrices = await _priceList.GetPricesAsync(items.Keys.ToList(), systemPriceList, ct);
        }

        var rows = rawRows.Select(r => ProcessRow(r, config, parameters.Module, parameters.LineType, parameters.BusinessPartnerCardCode,
            validBusinessPartners, crossReferenceBySocio, items, warehouses, accounts, costCenters, dimension2, dimension3,
            userFieldsCatalog, systemPrices)).ToList();

        var documents = rows
            .GroupBy(r => r.GroupingKey)
            .Select(g => new GenericImportDocumentDto { GroupingKey = g.Key, Rows = g.ToList() })
            .ToList();

        return new GenericImportResultDto { HasValidConfig = true, Documents = documents };
    }

    public async Task<byte[]> GenerateTemplateAsync(GenericImportParametersDto parameters, CancellationToken ct = default)
    {
        var config = await _config.ResolveAsync(parameters.Module, parameters.DocumentType, parameters.LineType,
            parameters.BusinessPartnerCardCode, ct);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Formato");

        if (config is not null)
        {
            var userFieldsCatalog = (await _userFieldsCatalog.ListAsync(parameters.Module, ct)).ToDictionary(f => f.Id);

            foreach (var field in config.Fields.Where(f => !string.IsNullOrEmpty(f.ExcelColumn)))
            {
                var index = ColumnLetterToIndex(field.ExcelColumn!);
                var label = field.LogicalField == GenericImportLogicalField.UserField
                    && field.UserFieldId is { } userFieldId
                    && userFieldsCatalog.TryGetValue(userFieldId, out var userField)
                        ? userField.Label
                        : field.LogicalField.ToString();
                ws.Cell(1, index + 1).Value = field.IsRequired ? $"{label}*" : label;
            }
        }
        else
        {
            var columns = DefaultGenericColumns(parameters.Module, parameters.LineType);
            for (var i = 0; i < columns.Count; i++)
            {
                ws.Cell(1, i + 1).Value = columns[i];
            }
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static List<string> DefaultGenericColumns(GenericImportModule module, GenericImportLineType lineType)
    {
        if (module == GenericImportModule.Inventory)
        {
            return ["Cantidad", "CodigoArticulo", "AlmacenOrigen", "AlmacenDestino"];
        }

        var columns = new List<string> { "OrdenCompraSocio" };
        if (module == GenericImportModule.Sales)
        {
            columns.Add("Sucursal");
        }

        columns.Add("Cantidad");
        columns.Add("PrecioUnitario");
        columns.Add("PorcentajeDescuento");

        if (lineType == GenericImportLineType.Item)
        {
            columns.Add("CodigoArticulo");
            columns.Add("Bodega");
        }
        else
        {
            columns.Add("Descripcion");
            columns.Add("CuentaMayor");
            columns.Add("CentroCostos");
            columns.Add("Dimension2");
            columns.Add("Dimension3");
        }

        return columns;
    }

    public async Task<byte[]> GenerateFileWithErrorsAsync(GenericImportParametersDto parameters,
        IReadOnlyList<GenericImportDocumentDto> documents, CancellationToken ct = default)
    {
        var config = await _config.ResolveAsync(parameters.Module, parameters.DocumentType, parameters.LineType,
            parameters.BusinessPartnerCardCode, ct)
            ?? throw new InvalidOperationException("No hay una configuración vigente para regenerar el archivo.");

        var userFieldsCatalog = (await _userFieldsCatalog.ListAsync(parameters.Module, ct)).ToDictionary(f => f.Id);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Datos");

        var mappedFields = config.Fields.Where(f => !string.IsNullOrEmpty(f.ExcelColumn)).ToList();
        foreach (var field in mappedFields)
        {
            var index = ColumnLetterToIndex(field.ExcelColumn!);
            var label = FieldLabel(field, userFieldsCatalog);
            ws.Cell(1, index + 1).Value = field.IsRequired ? $"{label}*" : label;
        }

        // "Errores" siempre al final, después de la última columna mapeada -- nunca pisa
        // una columna real del layout de la configuración vigente.
        var errorsColumnIndex = (mappedFields.Count > 0 ? mappedFields.Max(f => ColumnLetterToIndex(f.ExcelColumn!)) : -1) + 1;
        ws.Cell(1, errorsColumnIndex + 1).Value = "Errores";

        // Los documentos ya vienen agrupados por ClaveAgrupacion -- hay que deshacer eso
        // para que el archivo salga en el mismo orden que tenía el original.
        var allRows = documents.SelectMany(d => d.Rows).OrderBy(r => r.RowNumber).ToList();

        var rowIndex = 1;
        foreach (var row in allRows)
        {
            rowIndex++;
            foreach (var field in mappedFields)
            {
                var index = ColumnLetterToIndex(field.ExcelColumn!);
                ws.Cell(rowIndex, index + 1).Value = FieldValue(row, field, userFieldsCatalog) ?? string.Empty;
            }

            ws.Cell(rowIndex, errorsColumnIndex + 1).Value = string.Join("; ", row.Errors);

            if (!row.IsValid)
            {
                ws.Range(rowIndex, 1, rowIndex, errorsColumnIndex + 1).Style.Fill.BackgroundColor = XLColor.FromArgb(255, 214, 214);
            }
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string FieldLabel(GenericImportConfigFieldDto field, IReadOnlyDictionary<int, GenericImportUserFieldDto> userFieldsCatalog) =>
        field.LogicalField == GenericImportLogicalField.UserField && field.UserFieldId is { } userFieldId
            && userFieldsCatalog.TryGetValue(userFieldId, out var userField)
            ? userField.Label
            : field.LogicalField.ToString();

    /// <summary>
    /// Valor a escribir en el archivo con errores -- crudo del Excel original para
    /// campos núcleo (ver GenericImportRowDto.RawValues, necesario porque el campo ya
    /// resuelto queda null justo en las filas con error) o el valor ya parseado para
    /// campos de usuario (que no guardan valor crudo aparte -- aceptable porque esos
    /// campos nunca fallan contra un catálogo SAP, a diferencia de los núcleo).
    /// </summary>
    private static string? FieldValue(GenericImportRowDto row, GenericImportConfigFieldDto field, IReadOnlyDictionary<int, GenericImportUserFieldDto> userFieldsCatalog)
    {
        if (field.LogicalField == GenericImportLogicalField.UserField && field.UserFieldId is { } userFieldId
            && userFieldsCatalog.TryGetValue(userFieldId, out var userField))
        {
            return (row.UserFieldsHeader.GetValueOrDefault(userField.SapFieldName) ?? row.UserFieldsLine.GetValueOrDefault(userField.SapFieldName))?.ToString();
        }

        return row.RawValues.GetValueOrDefault(field.LogicalField);
    }

    public async Task<GenericImportProgressDto> CreateDocumentsAsync(string jobId, string portalUsername,
        GenericImportParametersDto parameters, IReadOnlyList<GenericImportDocumentDto> documents, CancellationToken ct = default)
    {
        var creatable = documents.Where(d => d.CanCreate).ToList();
        if (creatable.Count == 0)
        {
            var none = new GenericImportProgressDto(
                "No hay documentos válidos para crear (revisá los errores de la vista previa).", 0, 0, false, null, true);
            await _progress.UpdateAsync(jobId, none);
            return none;
        }

        // La unidad de progreso es el LOTE de líneas, no el documento -- con un único
        // documento de miles de líneas (sin GroupingColumn configurado), "0 de 1" no le
        // dice nada al usuario mientras dura todo el proceso.
        var totalBatches = creatable.Sum(d => CountOfBatches(d.Rows.Count));
        var currentBatch = 0;

        await UpdateProgressAsync(jobId, $"Iniciando creación de {creatable.Count} documento(s)...", 0, totalBatches, true, null, false);

        var results = new List<GenericImportDocumentResultDto>();
        var processed = 0;

        foreach (var document in creatable)
        {
            processed++;
            var documentIndex = processed;
            var totalBatchesDocument = CountOfBatches(document.Rows.Count);

            async Task ReportBatch(int documentBatch)
            {
                currentBatch++;
                var lineDetail = totalBatchesDocument > 1
                    ? $" -- líneas {Math.Min(documentBatch * DefaultBatchSize, document.Rows.Count)}/{document.Rows.Count}"
                    : string.Empty;
                await UpdateProgressAsync(jobId,
                    $"Creando documento {documentIndex} de {creatable.Count} ({document.GroupingKey}){lineDetail}...",
                    currentBatch, totalBatches, true, null, false);
            }

            try
            {
                var docNum = parameters.Module switch
                {
                    GenericImportModule.Sales => await CreateSalesDocumentAsync(parameters, document, portalUsername, ReportBatch, ct),
                    GenericImportModule.Purchase => await CreatePurchaseDocumentAsync(parameters, document, portalUsername, ReportBatch, ct),
                    GenericImportModule.Inventory => await CreateInventoryDocumentAsync(parameters, document, portalUsername, ReportBatch, ct),
                    _ => throw new InvalidOperationException($"Módulo {parameters.Module} no soportado."),
                };

                results.Add(new GenericImportDocumentResultDto(document.GroupingKey, true, docNum, "Documento creado correctamente."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear el documento del grupo {GroupingKey} en el trabajo de import {JobId}.", document.GroupingKey, jobId);
                results.Add(new GenericImportDocumentResultDto(document.GroupingKey, false, 0, ex.Message));
            }
        }

        var successes = results.Count(r => r.Success);
        var final = new GenericImportProgressDto(
            $"Proceso completado: {successes} exitoso(s), {creatable.Count - successes} fallido(s).",
            totalBatches, totalBatches, successes == creatable.Count, null, true, results);
        await _progress.UpdateAsync(jobId, final);
        return final;
    }

    private static int CountOfBatches(int rowCount) => Math.Max(1, (int)Math.Ceiling(rowCount / (double)DefaultBatchSize));

    private async Task<int> CreateSalesDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        string portalUsername, Func<int, Task> reportBatch, CancellationToken ct)
    {
        var type = Enum.Parse<SalesDocumentType>(parameters.DocumentType);
        var first = document.Rows[0];
        // Todas las filas de un grupo ya comparten socio por construcción (ver
        // ProcessRow -- el prefijo de socio en GroupingKey garantiza esto), así que
        // alcanza con mirar la primera. Resuelto POR documento, no una vez para todo el
        // archivo -- en modo multi-socio cada documento puede ser de un socio distinto.
        var cardCode = BusinessPartnerCardCodeOfDocument(document, parameters);
        var partnerDefaults = !string.IsNullOrWhiteSpace(cardCode) ? await _partnerDefaults.GetAsync(cardCode, ct) : null;

        var lines = document.Rows.Select(r => new SalesDocumentLineDto(
            parameters.LineType == GenericImportLineType.Item ? DocumentLineType.Item : DocumentLineType.Service,
            ItemCode: r.ItemCode,
            Description: parameters.LineType == GenericImportLineType.Item ? (r.ItemName ?? r.ItemCode) : r.Description,
            Quantity: r.Quantity ?? 0,
            UnitPrice: r.UnitPrice,
            DiscountPercent: r.DiscountPercent ?? 0,
            WarehouseCode: r.Warehouse,
            AccountCode: r.Account,
            CostCenterCode: r.CostCenter,
            CostCenterCode2: r.Dimension2,
            CostCenterCode3: r.Dimension3,
            AdditionalFields: r.UserFieldsLine.Count > 0 ? r.UserFieldsLine : null)).ToList();

        var batches = Chunk(lines, DefaultBatchSize);

        var doc = new SalesDocumentDto(
            CustomerCardCode: cardCode,
            CustomerName: null,
            SalesEmployeeCode: partnerDefaults?.SalesEmployeeCode,
            Comments: null,
            DocDate: DateOnly.FromDateTime(DateTime.Today),
            DocDueDate: DateOnly.FromDateTime(DateTime.Today),
            TaxDate: DateOnly.FromDateTime(DateTime.Today),
            CustomerReferenceNumber: first.CustomerReferenceNumber,
            Lines: batches[0],
            AdditionalFields: first.UserFieldsHeader.Count > 0 ? first.UserFieldsHeader : null);

        var docEntry = await _sales.CreateAsync(type, portalUsername, doc, ct);
        await reportBatch(1);

        for (var i = 1; i < batches.Count; i++)
        {
            try
            {
                await _sales.AddLinesAsync(type, docEntry, batches[i], ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falló un lote posterior al primero del documento {GroupingKey} (DocEntry {DocEntry}) -- queda a medio cargar en SAP, revisar manualmente.",
                    document.GroupingKey, docEntry);
                throw;
            }
            await reportBatch(i + 1);
        }

        var created = await _sales.GetAsync(type, docEntry, ct);
        return created?.DocNum ?? 0;
    }

    private async Task<int> CreatePurchaseDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        string portalUsername, Func<int, Task> reportBatch, CancellationToken ct)
    {
        var type = Enum.Parse<PurchaseDocumentType>(parameters.DocumentType);
        var first = document.Rows[0];
        var cardCode = BusinessPartnerCardCodeOfDocument(document, parameters);

        var lines = document.Rows.Select(r => new PurchaseDocumentLineDto(
            parameters.LineType == GenericImportLineType.Item ? DocumentLineType.Item : DocumentLineType.Service,
            ItemCode: r.ItemCode,
            Description: parameters.LineType == GenericImportLineType.Item ? (r.ItemName ?? r.ItemCode) : r.Description,
            Quantity: r.Quantity ?? 0,
            UnitPrice: r.UnitPrice,
            DiscountPercent: r.DiscountPercent ?? 0,
            WarehouseCode: r.Warehouse,
            AccountCode: r.Account,
            CostCenterCode: r.CostCenter,
            CostCenterCode2: r.Dimension2,
            CostCenterCode3: r.Dimension3,
            AdditionalFields: r.UserFieldsLine.Count > 0 ? r.UserFieldsLine : null)).ToList();

        var batches = Chunk(lines, DefaultBatchSize);

        var doc = new PurchaseDocumentDto(
            SupplierCardCode: cardCode,
            SupplierName: null,
            Comments: null,
            DocDate: DateOnly.FromDateTime(DateTime.Today),
            DocDueDate: DateOnly.FromDateTime(DateTime.Today),
            TaxDate: DateOnly.FromDateTime(DateTime.Today),
            SupplierReferenceNumber: first.CustomerReferenceNumber,
            Lines: batches[0],
            AdditionalFields: first.UserFieldsHeader.Count > 0 ? first.UserFieldsHeader : null);

        var docEntry = await _purchase.CreateAsync(type, portalUsername, doc, ct);
        await reportBatch(1);

        for (var i = 1; i < batches.Count; i++)
        {
            try
            {
                await _purchase.AddLinesAsync(type, docEntry, batches[i], ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo un lote posterior al primero del documento {GroupingKey} (DocEntry {DocEntry}) -- queda a medio cargar en SAP, revisar manualmente.",
                    document.GroupingKey, docEntry);
                throw;
            }
            await reportBatch(i + 1);
        }

        var created = await _purchase.GetAsync(type, docEntry, ct);
        return created?.DocNum ?? 0;
    }

    private async Task<int> CreateInventoryDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        string portalUsername, Func<int, Task> reportBatch, CancellationToken ct)
    {
        var type = Enum.Parse<InventoryDocumentType>(parameters.DocumentType);
        var first = document.Rows[0];
        // Socio opcional en Inventario (traslado interno sin cliente/proveedor) --
        // BusinessPartnerCardCodeOfDocument devuelve "" si no hay ninguno, y ahí queda
        // como null/vacío en el documento SAP (mismo criterio que ya usa esta pantalla
        // para Venta/Compra, donde sí es obligatorio).
        var cardCode = BusinessPartnerCardCodeOfDocument(document, parameters);

        var lines = document.Rows.Select(r => new InventoryDocumentLineDto(
            ItemCode: r.ItemCode ?? string.Empty,
            Description: r.ItemName ?? r.ItemCode,
            Quantity: r.Quantity ?? 0,
            FromWarehouseCode: r.SourceWarehouse ?? string.Empty,
            ToWarehouseCode: r.DestinationWarehouse ?? string.Empty,
            AdditionalFields: r.UserFieldsLine.Count > 0 ? r.UserFieldsLine : null)).ToList();

        var batches = Chunk(lines, DefaultBatchSize);

        var doc = new InventoryDocumentDto(
            DocDate: DateOnly.FromDateTime(DateTime.Today),
            Comments: null,
            Lines: batches[0],
            BusinessPartnerCardCode: string.IsNullOrWhiteSpace(cardCode) ? null : cardCode,
            CustomerReferenceNumber: first.CustomerReferenceNumber,
            AdditionalFields: first.UserFieldsHeader.Count > 0 ? first.UserFieldsHeader : null);

        var docEntry = await _inventory.CreateAsync(type, portalUsername, doc, ct);
        await reportBatch(1);

        for (var i = 1; i < batches.Count; i++)
        {
            try
            {
                await _inventory.AddLinesAsync(type, docEntry, batches[i], ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo un lote posterior al primero del documento {GroupingKey} (DocEntry {DocEntry}) -- queda a medio cargar en SAP, revisar manualmente.",
                    document.GroupingKey, docEntry);
                throw;
            }
            await reportBatch(i + 1);
        }

        var created = await _inventory.GetAsync(type, docEntry, ct);
        return created?.DocNum ?? 0;
    }

    /// <summary>Trocea una lista en sublistas de a lo sumo <paramref name="batchSize"/> elementos -- siempre devuelve al menos 1 lote (vacío) para que el primer POST de creación tenga algo que mandar aunque no haya filas.</summary>
    private static List<List<T>> Chunk<T>(IReadOnlyList<T> items, int batchSize)
    {
        if (items.Count == 0)
        {
            return [[]];
        }

        var batches = new List<List<T>>();
        for (var i = 0; i < items.Count; i += batchSize)
        {
            batches.Add(items.Skip(i).Take(batchSize).ToList());
        }
        return batches;
    }

    private Task UpdateProgressAsync(string jobId, string message, int current, int total, bool success, string? details, bool finished) =>
        _progress.UpdateAsync(jobId, new GenericImportProgressDto(message, current, total, success, details, finished));

    /// <summary>
    /// CardCode real de ESTE documento -- todas las filas de un grupo ya comparten
    /// socio por construcción (ver ProcessRow, el prefijo de socio en GroupingKey), así
    /// que alcanza con mirar la primera fila. Cae al socio fijo del wizard
    /// (parameters.BusinessPartnerCardCode) cuando la fila no trae uno propio (modo
    /// normal, sin carga multi-socio).
    /// </summary>
    private static string BusinessPartnerCardCodeOfDocument(GenericImportDocumentDto document, GenericImportParametersDto parameters) =>
        document.Rows[0].BusinessPartnerCardCode ?? parameters.BusinessPartnerCardCode ?? string.Empty;

    // -------------------------------------------------------------------------------
    // Resolución fila a fila
    // -------------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, string> EmptyCrossReference = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static GenericImportRowDto ProcessRow(RawRow raw, GenericImportConfigDto config, GenericImportModule module,
        GenericImportLineType lineType, string? parameterBusinessPartnerCardCode, IReadOnlyDictionary<string, string> validBusinessPartners,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> crossReferenceBySocio, IReadOnlyDictionary<string, ItemDto> items,
        IReadOnlyDictionary<string, WarehouseDto> warehouses, IReadOnlyDictionary<string, GeneralLedgerAccountDto> accounts,
        IReadOnlyDictionary<string, CostCenterDto> costCenters, IReadOnlyDictionary<string, CostCenterDto> dimension2, IReadOnlyDictionary<string, CostCenterDto> dimension3,
        IReadOnlyDictionary<int, GenericImportUserFieldDto> userFieldsCatalog, IReadOnlyDictionary<string, decimal> systemPrices)
    {
        var errors = new List<string>();

        string? Get(GenericImportLogicalField field) => raw.CoreFields.GetValueOrDefault(field);

        // Carga multi-socio: lee el CardCode de LA FILA y lo valida contra
        // validBusinessPartners (error de fila si no existe) -- ese CardCode decide qué
        // diccionario de crossReferenceBySocio usa esta fila. En modo normal (false),
        // el socio es siempre el del wizard, comportamiento idéntico al de antes.
        string? businessPartnerCardCode = null;
        string? businessPartnerName = null;
        if (config.BusinessPartnerFromFile)
        {
            var rowCardCode = Get(GenericImportLogicalField.BusinessPartnerCardCode)?.Trim();
            if (string.IsNullOrEmpty(rowCardCode))
            {
                errors.Add("El socio de negocio es obligatorio.");
            }
            else if (!validBusinessPartners.TryGetValue(rowCardCode, out businessPartnerName))
            {
                errors.Add($"El socio de negocio \"{rowCardCode}\" no existe.");
            }
            else
            {
                businessPartnerCardCode = rowCardCode;
            }
        }

        var effectiveCardCode = businessPartnerCardCode ?? parameterBusinessPartnerCardCode ?? string.Empty;
        var crossReferenceBySku = crossReferenceBySocio.GetValueOrDefault(effectiveCardCode, EmptyCrossReference);

        var quantity = ParseDecimal(Get(GenericImportLogicalField.Quantity));
        if (quantity is null)
        {
            errors.Add("Cantidad es obligatoria.");
        }

        var unitPrice = ParseDecimal(Get(GenericImportLogicalField.UnitPrice));
        var discountPercent = ParseDecimal(Get(GenericImportLogicalField.DiscountPercent)) ?? 0m;

        string? itemCode = null;
        string? itemName = null;
        string? warehouse = null;
        string? warehouseName = null;
        string? description = null;
        string? account = null;
        string? accountName = null;
        string? costCenter = null;
        string? costCenterName = null;
        string? dim2 = null;
        string? dim2Name = null;
        string? dim3 = null;
        string? dim3Name = null;
        string? sourceWarehouse = null;
        string? sourceWarehouseName = null;
        string? destinationWarehouse = null;
        string? destinationWarehouseName = null;

        if (module == GenericImportModule.Inventory)
        {
            var excelItemCode = Get(GenericImportLogicalField.ItemCode)?.Trim();
            if (string.IsNullOrEmpty(excelItemCode))
            {
                errors.Add("CodigoArticulo es obligatorio.");
            }
            else if (config.SkuIsCustomerOwn)
            {
                if (!crossReferenceBySku.TryGetValue(excelItemCode, out var resolvedItemCode))
                {
                    errors.Add($"El SKU \"{excelItemCode}\" no tiene paridad configurada para este socio de negocio.");
                }
                else if (!items.TryGetValue(resolvedItemCode, out var itemFromCrossReference))
                {
                    errors.Add($"El artículo \"{resolvedItemCode}\" (SKU \"{excelItemCode}\") no existe en SAP.");
                }
                else
                {
                    itemCode = itemFromCrossReference.ItemCode;
                    itemName = itemFromCrossReference.ItemName;
                }
            }
            else if (!items.TryGetValue(excelItemCode, out var directItem))
            {
                errors.Add($"El artículo \"{excelItemCode}\" no existe.");
            }
            else
            {
                itemCode = directItem.ItemCode;
                itemName = directItem.ItemName;
            }

            var sourceCode = Get(GenericImportLogicalField.SourceWarehouse)?.Trim();
            if (string.IsNullOrEmpty(sourceCode))
            {
                errors.Add("AlmacenOrigen es obligatorio.");
            }
            else if (!warehouses.TryGetValue(sourceCode, out var sourceWarehouseDto))
            {
                errors.Add($"El almacén de origen \"{sourceCode}\" no existe.");
            }
            else
            {
                sourceWarehouse = sourceWarehouseDto.WarehouseCode;
                sourceWarehouseName = sourceWarehouseDto.WarehouseName;
            }

            var destinationCode = Get(GenericImportLogicalField.DestinationWarehouse)?.Trim();
            if (string.IsNullOrEmpty(destinationCode))
            {
                errors.Add("AlmacenDestino es obligatorio.");
            }
            else if (!warehouses.TryGetValue(destinationCode, out var destinationWarehouseDto))
            {
                errors.Add($"El almacén de destino \"{destinationCode}\" no existe.");
            }
            else
            {
                destinationWarehouse = destinationWarehouseDto.WarehouseCode;
                destinationWarehouseName = destinationWarehouseDto.WarehouseName;
            }
        }
        else if (lineType == GenericImportLineType.Item)
        {
            var excelCode = Get(GenericImportLogicalField.ItemCode)?.Trim();
            if (string.IsNullOrEmpty(excelCode))
            {
                errors.Add("CodigoArticulo es obligatorio.");
            }
            else if (config.SkuIsCustomerOwn)
            {
                if (!crossReferenceBySku.TryGetValue(excelCode, out var resolvedItemCode))
                {
                    errors.Add($"El SKU \"{excelCode}\" no tiene paridad configurada para este socio de negocio.");
                }
                else if (!items.TryGetValue(resolvedItemCode, out var item))
                {
                    errors.Add($"El artículo \"{resolvedItemCode}\" (SKU \"{excelCode}\") no existe en SAP.");
                }
                else
                {
                    itemCode = item.ItemCode;
                    itemName = item.ItemName;
                }
            }
            else if (!items.TryGetValue(excelCode, out var directItem))
            {
                errors.Add($"El artículo \"{excelCode}\" no existe.");
            }
            else
            {
                itemCode = directItem.ItemCode;
                itemName = directItem.ItemName;
            }

            var warehouseCode = Get(GenericImportLogicalField.Warehouse)?.Trim();
            if (string.IsNullOrEmpty(warehouseCode))
            {
                errors.Add("Bodega es obligatoria.");
            }
            else if (!warehouses.TryGetValue(warehouseCode, out var warehouseDto))
            {
                errors.Add($"La bodega \"{warehouseCode}\" no existe.");
            }
            else
            {
                warehouse = warehouseDto.WarehouseCode;
                warehouseName = warehouseDto.WarehouseName;
            }
        }
        else
        {
            description = Get(GenericImportLogicalField.Description)?.Trim();
            if (string.IsNullOrEmpty(description))
            {
                errors.Add("Descripcion es obligatoria.");
            }

            var accountCode = Get(GenericImportLogicalField.Account)?.Trim();
            if (string.IsNullOrEmpty(accountCode))
            {
                errors.Add("CuentaMayor es obligatoria.");
            }
            else if (!accounts.TryGetValue(accountCode, out var accountDto))
            {
                errors.Add($"La cuenta mayor \"{accountCode}\" no existe.");
            }
            else
            {
                account = accountDto.AccountCode;
                accountName = accountDto.AccountName;
            }

            var costCenterCode = Get(GenericImportLogicalField.CostCenter)?.Trim();
            if (string.IsNullOrEmpty(costCenterCode))
            {
                errors.Add("CentroCostos es obligatorio.");
            }
            else if (!costCenters.TryGetValue(costCenterCode, out var costCenterDto))
            {
                errors.Add($"El centro de costos \"{costCenterCode}\" no existe.");
            }
            else
            {
                costCenter = costCenterDto.Code;
                costCenterName = costCenterDto.Name;
            }

            var dim2Code = Get(GenericImportLogicalField.Dimension2)?.Trim();
            if (!string.IsNullOrEmpty(dim2Code))
            {
                if (!dimension2.TryGetValue(dim2Code, out var d2))
                {
                    errors.Add($"Dimensión 2 \"{dim2Code}\" no existe.");
                }
                else
                {
                    dim2 = d2.Code;
                    dim2Name = d2.Name;
                }
            }

            var dim3Code = Get(GenericImportLogicalField.Dimension3)?.Trim();
            if (!string.IsNullOrEmpty(dim3Code))
            {
                if (!dimension3.TryGetValue(dim3Code, out var d3))
                {
                    errors.Add($"Dimensión 3 \"{dim3Code}\" no existe.");
                }
                else
                {
                    dim3 = d3.Code;
                    dim3Name = d3.Name;
                }
            }
        }

        var userFieldsHeader = new Dictionary<string, object?>();
        var userFieldsLine = new Dictionary<string, object?>();
        foreach (var (userFieldId, rawValue) in raw.UserFields)
        {
            if (!userFieldsCatalog.TryGetValue(userFieldId, out var definition))
            {
                continue;
            }

            var parsedValue = ParseUserFieldValue(rawValue, definition.DataType);
            var target = definition.Level == GenericImportFieldLevel.Header ? userFieldsHeader : userFieldsLine;
            target[definition.SapFieldName] = parsedValue;
        }

        // Precio de sistema: solo pisa cuando el archivo no trajo precio para esta
        // línea -- si el Excel sí trae UnitPrice, ese valor manda igual.
        if (unitPrice is null && itemCode is not null && systemPrices.TryGetValue(itemCode, out var systemListPrice))
        {
            unitPrice = systemListPrice;
        }

        var groupingKeyBase = string.IsNullOrEmpty(config.GroupingColumn) ? SingleGroupingKey : (raw.RawGroupingKey ?? SingleGroupingKey);
        // Prefijo de socio en modo multi-socio -- garantiza que un documento SAP nunca
        // mezcle dos CardCode distintos, aunque coincida el resto de la clave de
        // agrupación (ej. misma Sucursal de dos socios distintos). Preserva intacto el
        // caso ya soportado (mismo socio, varias sucursales -> un documento) porque no
        // reemplaza groupingKeyBase, solo lo antepone.
        var groupingKey = config.BusinessPartnerFromFile ? $"{effectiveCardCode}|{groupingKeyBase}" : groupingKeyBase;

        var row = new GenericImportRowDto
        {
            RowNumber = raw.RowNumber,
            GroupingKey = groupingKey,
            BusinessPartnerCardCode = businessPartnerCardCode,
            BusinessPartnerName = businessPartnerName,
            CustomerReferenceNumber = Get(GenericImportLogicalField.CustomerReferenceNumber),
            Branch = Get(GenericImportLogicalField.Branch),
            ItemCode = itemCode,
            ItemName = itemName,
            Description = description,
            Quantity = quantity,
            UnitPrice = unitPrice,
            DiscountPercent = discountPercent,
            Warehouse = warehouse,
            WarehouseName = warehouseName,
            Account = account,
            AccountName = accountName,
            CostCenter = costCenter,
            CostCenterName = costCenterName,
            Dimension2 = dim2,
            Dimension2Name = dim2Name,
            Dimension3 = dim3,
            Dimension3Name = dim3Name,
            SourceWarehouse = sourceWarehouse,
            SourceWarehouseName = sourceWarehouseName,
            DestinationWarehouse = destinationWarehouse,
            DestinationWarehouseName = destinationWarehouseName,
            UserFieldsHeader = userFieldsHeader,
            UserFieldsLine = userFieldsLine,
            RawValues = raw.CoreFields,
        };

        foreach (var rule in BuiltInRules)
        {
            errors.AddRange(rule.Validate(row));
        }

        return row with { IsValid = errors.Count == 0, Errors = errors };
    }

    private static object? ParseUserFieldValue(string? rawValue, GenericImportFieldDataType dataType)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return dataType switch
        {
            GenericImportFieldDataType.Number => (object?)ParseDecimal(rawValue),
            GenericImportFieldDataType.Date => ParseDate(rawValue),
            _ => rawValue.Trim(),
        };
    }

    /// <summary>Prueba es-CL (miles ".", decimal ",") y cae a invariante.</summary>
    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CL"), out var esCl))
        {
            return esCl;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant) ? invariant : null;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("es-CL"), DateTimeStyles.None, out var date))
        {
            return date;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var invariantDate) ? invariantDate : null;
    }

    // -------------------------------------------------------------------------------
    // Lectura del archivo
    // -------------------------------------------------------------------------------

    private sealed class RawRow
    {
        public int RowNumber { get; init; }
        public Dictionary<GenericImportLogicalField, string?> CoreFields { get; } = new();
        public Dictionary<int, string?> UserFields { get; } = new();
        public string? RawGroupingKey { get; set; }
    }

    private static DataTable ReadExcel(Stream excel)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        using var reader = ExcelReaderFactory.CreateReader(excel);
        // UseHeaderRow = true -- el formato estándar mapea columnas por LETRA (A, B,
        // C...), no por nombre de encabezado, pero se asume que la primera fila del
        // archivo es un encabezado humano a saltar -- las columnas siguen siendo
        // posicionales (fila.ItemArray[índice]), esto solo excluye la fila 1 de
        // tabla.Rows.
        var result = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true },
        });

        if (result.Tables.Count == 0)
        {
            throw new InvalidOperationException("El archivo no contiene ninguna hoja válida.");
        }

        return result.Tables[0];
    }

    private static List<RawRow> ReadRawRows(DataTable table, GenericImportConfigDto config)
    {
        var fieldsWithColumn = config.Fields.Where(f => f.LogicalField != GenericImportLogicalField.UserField).ToList();
        var userFields = config.Fields.Where(f => f.LogicalField == GenericImportLogicalField.UserField && f.UserFieldId is not null).ToList();

        var groupingIndex = string.IsNullOrEmpty(config.GroupingColumn) ? -1 : ColumnLetterToIndex(config.GroupingColumn);

        var rows = new List<RawRow>();
        var rowNumber = 0;
        foreach (DataRow row in table.Rows)
        {
            rowNumber++;

            // Fila vacía -- ninguna celda del primer par de columnas tiene valor.
            if (row.ItemArray.Length > 0 && row[0] == DBNull.Value && (row.ItemArray.Length < 2 || row[1] == DBNull.Value))
            {
                continue;
            }

            string? CellAt(string? letter)
            {
                if (string.IsNullOrWhiteSpace(letter))
                {
                    return null;
                }
                var index = ColumnLetterToIndex(letter);
                var value = index >= 0 && index < row.ItemArray.Length && row[index] != DBNull.Value ? row[index].ToString()!.Trim() : string.Empty;
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            var rawRow = new RawRow { RowNumber = rowNumber };

            foreach (var field in fieldsWithColumn)
            {
                rawRow.CoreFields[field.LogicalField] = field.FixedValue is { Length: > 0 } ? field.FixedValue : CellAt(field.ExcelColumn);
            }

            foreach (var field in userFields)
            {
                rawRow.UserFields[field.UserFieldId!.Value] = field.FixedValue is { Length: > 0 } ? field.FixedValue : CellAt(field.ExcelColumn);
            }

            if (groupingIndex >= 0)
            {
                rawRow.RawGroupingKey = groupingIndex < row.ItemArray.Length && row[groupingIndex] != DBNull.Value
                    ? row[groupingIndex].ToString()!.Trim()
                    : null;
            }

            rows.Add(rawRow);
        }

        return rows;
    }

    /// <summary>Convierte letras de columna Excel ("A", "B", "Z") a índices de C# (0, 1, 25).</summary>
    private static int ColumnLetterToIndex(string letter)
    {
        var result = 0;
        foreach (var c in letter.ToUpperInvariant())
        {
            result = result * 26 + (c - 'A' + 1);
        }
        return result - 1;
    }
}
