using System.Data;
using System.Globalization;
using ClosedXML.Excel;
using ExcelDataReader;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica.Reglas;

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
    private readonly IGenericImportValidationRuleEngine _validationEngine;
    private readonly IItemStockService _stock;
    private readonly ISalesEmployeeCatalogService _salesEmployees;
    private readonly IHanaService _hana;
    private readonly ILogger<GenericImportService> _logger;

    public GenericImportService(IGenericImportConfigService config, IGenericImportUserFieldService userFieldsCatalog,
        IBusinessPartnerDefaultsService partnerDefaults, IItemCrossReferenceService crossReference, IItemCatalogService items,
        IWarehouseCatalogService warehouses, IGeneralLedgerAccountCatalogService accounts, ICostCenterCatalogService costCenters,
        IPriceListService priceList, ICustomerCatalogService customers, ISupplierCatalogService suppliers,
        ISalesDocumentService sales, IPurchaseDocumentService purchase, IInventoryDocumentService inventory,
        IGenericImportProgressStore progress, IGenericImportValidationRuleEngine validationEngine, IItemStockService stock,
        ISalesEmployeeCatalogService salesEmployees, IHanaService hana, ILogger<GenericImportService> logger)
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
        _validationEngine = validationEngine;
        _stock = stock;
        _salesEmployees = salesEmployees;
        _hana = hana;
        _logger = logger;
    }

    public async Task<GenericImportResultDto> ProcessFileAsync(GenericImportParametersDto parameters, Stream file, CancellationToken ct = default)
    {
        var config = await _config.ResolveAsync(parameters.Module, parameters.DocumentType, parameters.LineType,
            parameters.BusinessPartnerCardCode, parameters.ConfigId, ct);
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

        // Almacén del cliente (campo de usuario de OCRD) -- pisa la columna/valor fijo de la
        // grilla ANTES de cualquier resolución, así el resto del pipeline (catálogo de
        // almacenes, stock, creación) lo trata igual que si viniera en el archivo.
        if (parameters.Module != GenericImportModule.Inventory && parameters.LineType == GenericImportLineType.Item
            && config.DocumentOptions.WarehouseSource == GenericImportWarehouseSource.BusinessPartnerField)
        {
            await ApplyBusinessPartnerWarehouseAsync(rawRows, config, parameters, ct);
        }

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

        var excelItemCodes = CodesOf(GenericImportLogicalField.ItemCode);

        // Traducción "código del archivo -> ItemCode" según config.ItemIdentifier:
        // - CustomerSku: paridad SKU-socio, una llamada POR CADA socio distinto de
        //   businessPartnersInFile (con un único socio, exactamente 1 llamada).
        // - Barcode: OITM.CodeBars + OBCD, UNA consulta para todo el archivo (la barra no
        //   depende del socio) -- se guarda bajo BarcodeTranslationKey.
        // - ItemCode: sin traducción, el archivo ya trae el ItemCode.
        var crossReferenceBySocio = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (config.ItemIdentifier == GenericImportItemIdentifier.Barcode && excelItemCodes.Count > 0)
        {
            crossReferenceBySocio[BarcodeTranslationKey] = await _items.GetItemCodesByBarcodesAsync(excelItemCodes, ct);
        }
        else if (config.ItemIdentifier == GenericImportItemIdentifier.CustomerSku)
        {
            foreach (var cardCode in businessPartnersInFile)
            {
                crossReferenceBySocio[cardCode] = (await _crossReference.ListAsync(cardCode, ct))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
                    .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().ItemCode, StringComparer.OrdinalIgnoreCase);
            }
        }

        // Unión de todos los ItemCode de todas las traducciones resueltas -- trae de más si
        // hay SKUs sin paridad en algún socio, pero evita una segunda pasada.
        var itemCodesToResolve = config.ItemIdentifier != GenericImportItemIdentifier.ItemCode
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

        // Precio SAP + stock disponible SIEMPRE (no solo con la regla activa) -- los usan
        // el análisis (comparación de precios, cumplimiento) y la regla de descuento
        // total de la OC, que corre en el motor de abajo.
        rows = await EnrichForAnalysisAsync(rows, config, parameters, ct);

        // DuplicateOrderInSap necesita el tipo de documento (para saber en qué tabla SAP
        // buscar la OC) y la interfaz de reglas no lo recibe -- se agrega a sus parámetros.
        var ruleAssignments = config.ValidationRules
            .Select(a => a.RuleType == GenericImportValidationRuleType.DuplicateOrderInSap
                ? a with { Parameters = new Dictionary<string, object?>(a.Parameters) { [DuplicateOrderInSapRule.DocumentTypeParameter] = parameters.DocumentType } }
                : a)
            .ToList();
        rows = (await _validationEngine.ApplyAsync(rows, parameters.Module, ruleAssignments, ct)).ToList();

        var documents = rows
            .GroupBy(r => r.GroupingKey)
            .Select(g => new GenericImportDocumentDto { GroupingKey = g.Key, Rows = g.ToList() })
            .ToList();

        return new GenericImportResultDto
        {
            HasValidConfig = true,
            Documents = documents,
            Analysis = GenericImportAnalysisBuilder.Build(documents, parameters.Module),
        };
    }

    public async Task<byte[]> GenerateTemplateAsync(GenericImportParametersDto parameters, CancellationToken ct = default)
    {
        var config = await _config.ResolveAsync(parameters.Module, parameters.DocumentType, parameters.LineType,
            parameters.BusinessPartnerCardCode, parameters.ConfigId, ct);

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
                        // La columna de artículo se nombra según qué trae (ver ItemIdentifier).
                        : field.LogicalField == GenericImportLogicalField.ItemCode && config.ItemIdentifier == GenericImportItemIdentifier.Barcode ? "Barcode"
                        : field.LogicalField == GenericImportLogicalField.ItemCode && config.ItemIdentifier == GenericImportItemIdentifier.CustomerSku ? "SKU"
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
            parameters.BusinessPartnerCardCode, parameters.ConfigId, ct)
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

    /// <summary>
    /// Reporte de validación (2026-10-14): las mismas grillas que la vista previa --
    /// Resumen por OC, Detalle de líneas y un panel por tipo de hallazgo (Sucursales,
    /// Artículos, Precios, Cumplimiento, Otras observaciones) -- armadas desde
    /// GenericImportAnalysisBuilder, la misma fuente que la pantalla.
    /// </summary>
    public Task<byte[]> GenerateValidationReportAsync(GenericImportParametersDto parameters,
        IReadOnlyList<GenericImportDocumentDto> documents, CancellationToken ct = default)
    {
        var analysis = GenericImportAnalysisBuilder.Build(documents, parameters.Module);
        var isInventory = parameters.Module == GenericImportModule.Inventory;
        var rows = documents.SelectMany(d => d.Rows).OrderBy(r => r.RowNumber).ToList();

        using var workbook = new XLWorkbook();

        // ---- Resumen por OC
        var orderHeaders = new List<string> { "OC / Grupo", "N° referencia", "Socio", "Nombre socio", "Sucursal", "Líneas", "Con error", "Con advertencia", "Unidades" };
        if (!isInventory)
        {
            orderHeaders.AddRange(["Total OC", "Total precio SAP", "Descuento total %"]);
        }
        orderHeaders.AddRange(["Unid. cubiertas por stock", "Estado"]);
        var orderRows = analysis.Orders.Select(o =>
        {
            var values = new List<object?> { o.GroupingKey, o.CustomerReferenceNumber, o.BusinessPartnerCardCode, o.BusinessPartnerName, o.Branches,
                o.Lines, o.LinesWithErrors, o.LinesWithWarnings, o.Units };
            if (!isInventory)
            {
                values.AddRange([o.OrderAmount, o.SapAmount, o.DiscountPercent]);
            }
            values.AddRange([o.UnitsFulfillable, o.CanCreate ? "Listo para crear" : "Con errores"]);
            return values;
        }).ToList();
        var orderTotals = new List<object?> { "TOTAL", null, null, null, null, analysis.Totals.Lines, analysis.Totals.LinesWithErrors,
            analysis.Totals.LinesWithWarnings, analysis.Totals.Units };
        if (!isInventory)
        {
            orderTotals.AddRange([analysis.Totals.OrderAmount, analysis.Totals.SapAmount, null]);
        }
        orderTotals.AddRange([analysis.Totals.UnitsFulfillable, null]);
        WriteSheet(workbook, "Resumen OC", orderHeaders, orderRows, orderTotals,
            rowFill: i => analysis.Orders[i].CanCreate ? null : ErrorFill);

        // ---- Detalle de líneas (misma grilla que la pantalla)
        var detailHeaders = new List<string> { "Fila", "OC / Grupo", "N° referencia", "Sucursal", "Código archivo", "Código SAP", "Descripción" };
        detailHeaders.AddRange(isInventory ? ["Almacén origen", "Almacén destino"] : ["Almacén"]);
        detailHeaders.Add("Unidades");
        if (!isInventory)
        {
            detailHeaders.AddRange(["Precio OC", "Precio SAP", "Total OC", "Total SAP"]);
        }
        detailHeaders.AddRange(["Stock disponible", "Unid. cubiertas", "Errores", "Advertencias"]);
        var detailRows = rows.Select(r =>
        {
            var values = new List<object?> { r.RowNumber, r.GroupingKey, r.CustomerReferenceNumber, r.Branch,
                r.RawValues.GetValueOrDefault(GenericImportLogicalField.ItemCode), r.ItemCode, r.ItemName ?? r.Description };
            values.AddRange(isInventory ? [r.SourceWarehouse ?? r.RawValues.GetValueOrDefault(GenericImportLogicalField.SourceWarehouse),
                    r.DestinationWarehouse ?? r.RawValues.GetValueOrDefault(GenericImportLogicalField.DestinationWarehouse)]
                : [r.Warehouse ?? r.RawValues.GetValueOrDefault(GenericImportLogicalField.Warehouse)]);
            values.Add(r.Quantity);
            if (!isInventory)
            {
                values.AddRange([r.UnitPrice, r.SapReferencePrice,
                    r.UnitPrice is null ? null : (r.Quantity ?? 0m) * r.UnitPrice.Value,
                    r.SapReferencePrice is null ? null : (r.Quantity ?? 0m) * r.SapReferencePrice.Value]);
            }
            values.AddRange([r.StockAvailable, r.FulfillableQuantity, string.Join("; ", r.Errors), string.Join("; ", r.Warnings)]);
            return values;
        }).ToList();
        var detailTotals = new List<object?> { "TOTAL", null, null, null, null, null, null };
        detailTotals.AddRange(isInventory ? [null, null] : [null]);
        detailTotals.Add(analysis.Totals.Units);
        if (!isInventory)
        {
            detailTotals.AddRange([null, null, analysis.Totals.OrderAmount, analysis.Totals.SapAmount]);
        }
        detailTotals.AddRange([null, analysis.Totals.UnitsFulfillable, null, null]);
        WriteSheet(workbook, "Detalle", detailHeaders, detailRows, detailTotals,
            rowFill: i => !rows[i].IsValid ? ErrorFill : rows[i].Warnings.Count > 0 ? WarningFill : null);

        // ---- Paneles de análisis
        WriteSheet(workbook, "Sucursales con error", ["Sucursal", "OC / Grupo", "Líneas", "Observación"],
            analysis.BranchIssues.Select(b => new List<object?> { b.Branch, b.Orders, b.Lines, b.Message }).ToList());

        WriteSheet(workbook, "Artículos con error", ["Código archivo", "Descripción", "OC / Grupo", "Líneas", "Unidades", "Observación"],
            analysis.ItemIssues.Select(i => new List<object?> { i.SourceCode, i.Description, i.Orders, i.Lines, i.Units, i.Message }).ToList());

        if (!isInventory)
        {
            WriteSheet(workbook, "Precios", ["Fila", "OC / Grupo", "Código SAP", "Descripción", "Unidades", "Precio OC", "Precio SAP", "Diferencia", "Diferencia %", "Fuera de tolerancia"],
                analysis.PriceComparisons.Select(p => new List<object?> { p.RowNumber, p.GroupingKey, p.ItemCode, p.Description, p.Quantity,
                    p.OrderPrice, p.SapPrice, p.Difference, p.DifferencePercent, p.OutOfTolerance ? "Sí" : "No" }).ToList(),
                rowFill: i => analysis.PriceComparisons[i].OutOfTolerance ? ErrorFill : null);
        }

        WriteSheet(workbook, "Cumplimiento", ["Código SAP", "Descripción", "Almacén", "Unid. OC", "Stock disponible", "Unid. cubiertas", "% cumplimiento"],
            analysis.Fulfillment.Select(x => new List<object?> { x.ItemCode, x.Description, x.Warehouse, x.UnitsOrdered, x.StockAvailable,
                x.UnitsFulfillable, x.FulfillmentPercent }).ToList(),
            [ "TOTAL", null, null, analysis.Fulfillment.Sum(x => x.UnitsOrdered), null, analysis.Fulfillment.Sum(x => x.UnitsFulfillable),
              analysis.Fulfillment.Sum(x => x.UnitsOrdered) == 0 ? null
                  : Math.Round(analysis.Fulfillment.Sum(x => x.UnitsFulfillable) / analysis.Fulfillment.Sum(x => x.UnitsOrdered) * 100m, 1) ],
            rowFill: i => analysis.Fulfillment[i].FulfillmentPercent < 100m ? (analysis.Fulfillment[i].FulfillmentPercent == 0m ? ErrorFill : WarningFill) : null);

        WriteSheet(workbook, "OC ya cargadas", ["OC / Grupo", "N° referencia", "Líneas", "Gravedad", "Observación"],
            analysis.DuplicateOrders.Select(d => new List<object?> { d.GroupingKey, d.CustomerReferenceNumber, d.Lines, d.IsError ? "Error" : "Advertencia", d.Message }).ToList(),
            rowFill: i => analysis.DuplicateOrders[i].IsError ? ErrorFill : WarningFill);

        WriteSheet(workbook, "Otras observaciones", ["Fila", "OC / Grupo", "Tipo", "Gravedad", "Observación"],
            analysis.OtherIssues.Select(o => new List<object?> { o.RowNumber, o.GroupingKey, IssueCategoryLabel(o.Category), o.IsError ? "Error" : "Advertencia", o.Message }).ToList(),
            rowFill: i => analysis.OtherIssues[i].IsError ? ErrorFill : WarningFill);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Task.FromResult(stream.ToArray());
    }

    private static readonly XLColor ErrorFill = XLColor.FromArgb(255, 214, 214);
    private static readonly XLColor WarningFill = XLColor.FromArgb(255, 243, 205);

    internal static string IssueCategoryLabel(GenericImportIssueCategory category) => category switch
    {
        GenericImportIssueCategory.BusinessPartner => "Socio de negocio",
        GenericImportIssueCategory.Branch => "Sucursal",
        GenericImportIssueCategory.Item => "Artículo",
        GenericImportIssueCategory.Warehouse => "Almacén",
        GenericImportIssueCategory.Quantity => "Cantidad",
        GenericImportIssueCategory.Price => "Precio",
        GenericImportIssueCategory.Stock => "Stock",
        GenericImportIssueCategory.OrderTotal => "Descuento total OC",
        GenericImportIssueCategory.DuplicateOrder => "OC ya cargada",
        GenericImportIssueCategory.Accounting => "Contabilidad",
        _ => "Otro",
    };

    /// <summary>Hoja con encabezado en negrita y filtro, filas tal cual, fila de totales opcional en negrita y relleno por fila opcional.</summary>
    private static void WriteSheet(XLWorkbook workbook, string name, IReadOnlyList<string> headers, IReadOnlyList<List<object?>> rows,
        IReadOnlyList<object?>? totals = null, Func<int, XLColor?>? rowFill = null)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var c = 0; c < headers.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }
        sheet.Row(1).Style.Font.Bold = true;

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                SetCell(sheet.Cell(r + 2, c + 1), rows[r][c]);
            }
            if (rowFill?.Invoke(r) is { } fill)
            {
                sheet.Range(r + 2, 1, r + 2, headers.Count).Style.Fill.BackgroundColor = fill;
            }
        }

        if (totals is not null && rows.Count > 0)
        {
            var totalRow = rows.Count + 2;
            for (var c = 0; c < totals.Count; c++)
            {
                SetCell(sheet.Cell(totalRow, c + 1), totals[c]);
            }
            sheet.Row(totalRow).Style.Font.Bold = true;
        }

        if (rows.Count > 0)
        {
            sheet.Range(1, 1, rows.Count + 1, headers.Count).SetAutoFilter();
        }
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
    }

    private static void SetCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case decimal d:
                cell.Value = d;
                // "#,##0.##" deja el separador decimal colgando en Excel cuando el número
                // no tiene decimales ("12.996,") -- formato según el valor: entero sin
                // decimales, el resto con 2.
                cell.Style.NumberFormat.Format = d == decimal.Truncate(d) ? "#,##0" : "#,##0.00";
                break;
            case int i:
                cell.Value = i;
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
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
            _progress.Update(jobId, none);
            return none;
        }

        // La unidad de progreso es el LOTE de líneas, no el documento -- con un único
        // documento de miles de líneas (sin GroupingColumn configurado), "0 de 1" no le
        // dice nada al usuario mientras dura todo el proceso.
        var totalBatches = creatable.Sum(d => CountOfBatches(d.Rows.Count));
        var currentBatch = 0;

        UpdateProgress(jobId, $"Iniciando creación de {creatable.Count} documento(s)...", 0, totalBatches, true, null, false);

        var results = new List<GenericImportDocumentResultDto>();
        var processed = 0;

        // Datos de cabecera de cada OC para el reporte final -- mismo análisis que la vista previa.
        var summaries = GenericImportAnalysisBuilder.Build(documents, parameters.Module).Orders.ToDictionary(o => o.GroupingKey);
        GenericImportDocumentResultDto WithSummary(GenericImportDocumentResultDto result)
        {
            if (!summaries.TryGetValue(result.GroupingKey, out var o))
            {
                return result;
            }
            return result with
            {
                CustomerReferenceNumber = o.CustomerReferenceNumber,
                Branches = o.Branches,
                BusinessPartnerCardCode = o.BusinessPartnerCardCode,
                BusinessPartnerName = o.BusinessPartnerName,
                SalesEmployee = o.SalesEmployee,
                Lines = o.Lines,
                Units = o.Units,
                OrderAmount = o.OrderAmount,
            };
        }

        foreach (var document in creatable)
        {
            processed++;
            var documentIndex = processed;
            var totalBatchesDocument = CountOfBatches(document.Rows.Count);

            void ReportBatch(int documentBatch)
            {
                currentBatch++;
                var lineDetail = totalBatchesDocument > 1
                    ? $" -- líneas {Math.Min(documentBatch * DefaultBatchSize, document.Rows.Count)}/{document.Rows.Count}"
                    : string.Empty;
                UpdateProgress(jobId,
                    $"Creando documento {documentIndex} de {creatable.Count} ({document.GroupingKey}){lineDetail}...",
                    currentBatch, totalBatches, true, null, false);
            }

            try
            {
                var (docEntry, docNum, docTotal) = parameters.Module switch
                {
                    GenericImportModule.Sales => await CreateSalesDocumentAsync(parameters, document, portalUsername, ReportBatch, ct),
                    GenericImportModule.Purchase => await CreatePurchaseDocumentAsync(parameters, document, portalUsername, ReportBatch, ct),
                    GenericImportModule.Inventory => await CreateInventoryDocumentAsync(parameters, document, portalUsername, ReportBatch, ct),
                    _ => throw new InvalidOperationException($"Módulo {parameters.Module} no soportado."),
                };

                results.Add(WithSummary(new GenericImportDocumentResultDto(document.GroupingKey, true, docNum, "Documento creado correctamente."))
                    with { DocEntry = docEntry, SapDocTotal = docTotal });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear el documento del grupo {GroupingKey} en el trabajo de import {JobId}.", document.GroupingKey, jobId);
                results.Add(WithSummary(new GenericImportDocumentResultDto(document.GroupingKey, false, 0, ex.Message)));
            }
        }

        // OC que no se enviaron por errores de validación -- también van al reporte, para
        // que el resultado final cuente TODAS las OC del archivo.
        foreach (var skipped in documents.Where(d => !d.CanCreate))
        {
            var errors = skipped.Rows.SelectMany(r => r.Errors).Distinct().Take(3).ToList();
            results.Add(WithSummary(new GenericImportDocumentResultDto(skipped.GroupingKey, false, 0,
                "No enviado: la OC tiene errores de validación. " + string.Join(" ", errors))) with { Status = "Skipped" });
        }

        var successes = results.Count(r => r.Success);
        var skippedCount = results.Count(r => r.Status == "Skipped");
        var final = new GenericImportProgressDto(
            $"Proceso completado: {successes} pedido(s) creado(s), {creatable.Count - successes} con error en SAP" +
            (skippedCount > 0 ? $", {skippedCount} no enviado(s) por errores de validación." : "."),
            totalBatches, totalBatches, successes == creatable.Count, null, true, results);
        _progress.Update(jobId, final);
        return final;
    }

    private static int CountOfBatches(int rowCount) => Math.Max(1, (int)Math.Ceiling(rowCount / (double)DefaultBatchSize));

    /// <summary>
    /// Reemplaza el almacén de cada fila por el del socio (OCRD.&lt;WarehouseUserField&gt;,
    /// ej. U_NX_WhsCode). Socio sin valor en ese campo: la fila conserva la columna/valor
    /// fijo de la grilla (y si tampoco hay, ProcessRow marca "Bodega es obligatoria").
    /// </summary>
    private async Task ApplyBusinessPartnerWarehouseAsync(List<RawRow> rawRows, GenericImportConfigDto config,
        GenericImportParametersDto parameters, CancellationToken ct)
    {
        var userField = config.DocumentOptions.WarehouseUserField ?? GenericImportDocumentOptionsDto.DefaultWarehouseUserField;
        // Se interpola en SQL -- mismo control de formato que al guardarlo.
        if (!GenericImportConfigService.IsValidUserFieldName(userField))
        {
            return;
        }

        string? CardCodeOf(RawRow raw) => config.BusinessPartnerFromFile
            ? raw.CoreFields.GetValueOrDefault(GenericImportLogicalField.BusinessPartnerCardCode)?.Trim()
            : parameters.BusinessPartnerCardCode?.Trim();

        var cardCodes = rawRows.Select(CardCodeOf).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cardCodes.Count == 0)
        {
            return;
        }

        var sqlParameters = new Dictionary<string, object?>();
        var placeholders = new List<string>();
        for (var i = 0; i < cardCodes.Count; i++)
        {
            placeholders.Add($":cardCode{i}");
            sqlParameters[$"cardCode{i}"] = cardCodes[i];
        }

        var sql = $"SELECT \"CardCode\", \"{userField}\" AS \"WhsCode\" FROM \"OCRD\" WHERE \"CardCode\" IN ({string.Join(",", placeholders)})";
        var warehouseByCard = (await _hana.QueryDynamicAsync(sql, sqlParameters, ct))
            .Select(r => (CardCode: r["CardCode"]?.ToString()?.Trim() ?? string.Empty, WhsCode: r["WhsCode"]?.ToString()?.Trim()))
            .Where(r => !string.IsNullOrEmpty(r.WhsCode))
            .GroupBy(r => r.CardCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().WhsCode!, StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawRows)
        {
            if (CardCodeOf(raw) is { Length: > 0 } cardCode && warehouseByCard.TryGetValue(cardCode, out var whsCode))
            {
                raw.CoreFields[GenericImportLogicalField.Warehouse] = whsCode;
            }
        }
    }

    /// <summary>Vendedor del documento según el modo del formato -- ver GenericImportSalesPersonMode.</summary>
    internal static int? ResolveSalesPerson(GenericImportDocumentOptionsDto options, int? chosenInWizard, int? partnerDefault) =>
        options.SalesPersonMode switch
        {
            GenericImportSalesPersonMode.Fixed => options.SalesPersonCode,
            GenericImportSalesPersonMode.MultiSeller => chosenInWizard ?? partnerDefault,
            _ => partnerDefault,
        };

    /// <summary>
    /// Completa en cada fila el precio SAP (SapReferencePrice) y el stock disponible /
    /// unidades cubiertas (StockAvailable / FulfillableQuantity). Consultas en bloque
    /// para todo el archivo (nunca por fila). Precio: lista de sistema si el formato usa
    /// PriceSource = System, si no la lista del socio de la fila (OCRD.ListNum).
    /// </summary>
    private async Task<List<GenericImportRowDto>> EnrichForAnalysisAsync(List<GenericImportRowDto> rows, GenericImportConfigDto config,
        GenericImportParametersDto parameters, CancellationToken ct)
    {
        var module = parameters.Module;

        // Datos del socio (lista de precio + vendedor por defecto), una consulta por socio distinto.
        var defaultsByCardCode = new Dictionary<string, BusinessPartnerDefaultsDto?>(StringComparer.OrdinalIgnoreCase);
        if (module != GenericImportModule.Inventory)
        {
            foreach (var cardCode in rows.Select(r => r.BusinessPartnerCardCode).Where(c => c is not null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                defaultsByCardCode[cardCode!] = await _partnerDefaults.GetAsync(cardCode!, ct);
            }
        }
        // ---- Precio SAP (Venta/Compra, líneas de artículo)
        var sapPriceByRow = new Dictionary<int, decimal>();
        if (module != GenericImportModule.Inventory)
        {
            var pricedRows = rows.Where(r => r.ItemCode is not null).ToList();
            int? fixedList = config.PriceSource == GenericImportPriceSource.System ? config.SystemPriceListCode : null;
            var listByCardCode = fixedList is null
                ? defaultsByCardCode.ToDictionary(kv => kv.Key, kv => kv.Value?.PriceListCode, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);

            var itemCodes = pricedRows.Select(r => r.ItemCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // Precio especial del socio (OSPP/SPP1) -- solo con PriceSource = SpecialPrice;
            // tiene prioridad sobre la lista del socio, igual que en SAP.
            var specialByCardCode = new Dictionary<string, IReadOnlyDictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase);
            if (config.PriceSource == GenericImportPriceSource.SpecialPrice && itemCodes.Count > 0)
            {
                foreach (var cardCode in defaultsByCardCode.Keys)
                {
                    specialByCardCode[cardCode] = await _priceList.GetSpecialPricesAsync(cardCode, itemCodes, DateTime.Today, ct);
                }
            }
            var pricesByList = new Dictionary<int, IReadOnlyDictionary<string, decimal>>();
            foreach (var listNum in (fixedList is { } f ? [f] : listByCardCode.Values.Where(v => v is not null).Select(v => v!.Value)).Distinct())
            {
                pricesByList[listNum] = await _priceList.GetPricesAsync(itemCodes, listNum, ct);
            }

            foreach (var row in pricedRows)
            {
                if (row.BusinessPartnerCardCode is { } special && specialByCardCode.TryGetValue(special, out var specialPrices)
                    && specialPrices.TryGetValue(row.ItemCode!, out var specialPrice))
                {
                    sapPriceByRow[row.RowNumber] = specialPrice;
                    continue;
                }

                var listNum = fixedList ?? (row.BusinessPartnerCardCode is { } c ? listByCardCode.GetValueOrDefault(c) : null);
                if (listNum is { } l && pricesByList.TryGetValue(l, out var prices) && prices.TryGetValue(row.ItemCode!, out var price))
                {
                    sapPriceByRow[row.RowNumber] = price;
                }
            }
        }

        // ---- Stock disponible por artículo + almacén (origen en Inventario), repartido
        // entre las líneas en el orden del archivo.
        string? WarehouseOf(GenericImportRowDto r) => module == GenericImportModule.Inventory ? r.SourceWarehouse : r.Warehouse;
        // Opcional por formato (AnalyzeStock) -- con miles de líneas es la consulta más pesada.
        var stockRows = config.DocumentOptions.AnalyzeStock
            ? rows.Where(r => r.ItemCode is not null && WarehouseOf(r) is not null).ToList()
            : [];
        var available = stockRows.Count == 0
            ? new Dictionary<(string ItemCode, string WhsCode), decimal>()
            : await _stock.GetAvailableStockAsync(stockRows.Select(r => (r.ItemCode!, WarehouseOf(r)!)).Distinct().ToList(), ct);
        var remaining = new Dictionary<(string, string), decimal>();

        // Nombres de vendedores (catálogo chico) -- solo si el formato no usa el del socio,
        // que ya viene con nombre en BusinessPartnerDefaultsDto.
        var salesEmployeeNames = module == GenericImportModule.Sales && config.DocumentOptions.SalesPersonMode != GenericImportSalesPersonMode.BusinessPartner
            ? (await _salesEmployees.ListAsync(ct: ct)).GroupBy(s => s.SalesEmployeeCode).ToDictionary(g => g.Key, g => g.First().SalesEmployeeName)
            : new Dictionary<int, string>();

        return rows.Select(row =>
        {
            decimal? stockAvailable = null;
            decimal? fulfillable = null;
            if (row.ItemCode is not null && WarehouseOf(row) is { } whs)
            {
                var key = (row.ItemCode, whs);
                stockAvailable = Math.Max(available.GetValueOrDefault(key, 0m), 0m);
                var left = remaining.TryGetValue(key, out var r) ? r : stockAvailable.Value;
                var quantity = row.Quantity ?? 0m;
                fulfillable = Math.Max(Math.Min(quantity, left), 0m);
                remaining[key] = left - quantity;
            }

            int? salesEmployeeCode = null;
            string? salesEmployeeName = null;
            if (module == GenericImportModule.Sales)
            {
                var defaults = row.BusinessPartnerCardCode is { } bp ? defaultsByCardCode.GetValueOrDefault(bp) : null;
                salesEmployeeCode = ResolveSalesPerson(config.DocumentOptions, parameters.SalesPersonCode, defaults?.SalesEmployeeCode);
                salesEmployeeName = salesEmployeeCode is { } code
                    ? (code == defaults?.SalesEmployeeCode ? defaults?.SalesEmployeeName : salesEmployeeNames.GetValueOrDefault(code))
                    : null;
            }

            return row with
            {
                SalesEmployeeCode = salesEmployeeCode,
                SalesEmployeeName = salesEmployeeName,
                SapReferencePrice = sapPriceByRow.TryGetValue(row.RowNumber, out var sapPrice) ? sapPrice : row.SapReferencePrice,
                StockAvailable = stockAvailable,
                FulfillableQuantity = fulfillable,
            };
        }).ToList();
    }

    public async Task<SapSimulationResult> SimulateDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        string portalUsername, CancellationToken ct = default)
    {
        return parameters.Module switch
        {
            GenericImportModule.Sales => await _sales.SimulateCreateAsync(Enum.Parse<SalesDocumentType>(parameters.DocumentType),
                portalUsername, await BuildSalesDocumentAsync(parameters, document, ct), ct),
            GenericImportModule.Purchase => await _purchase.SimulateCreateAsync(Enum.Parse<PurchaseDocumentType>(parameters.DocumentType),
                portalUsername, BuildPurchaseDocument(parameters, document), ct),
            GenericImportModule.Inventory => await _inventory.SimulateCreateAsync(Enum.Parse<InventoryDocumentType>(parameters.DocumentType),
                portalUsername, BuildInventoryDocument(parameters, document), ct),
            _ => throw new InvalidOperationException("Módulo no soportado."),
        };
    }

    private async Task<(int DocEntry, int DocNum, decimal? DocTotal)> CreateSalesDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        string portalUsername, Action<int> reportBatch, CancellationToken ct)
    {
        var type = Enum.Parse<SalesDocumentType>(parameters.DocumentType);
        var fullDoc = await BuildSalesDocumentAsync(parameters, document, ct);
        var batches = Chunk(fullDoc.Lines.ToList(), DefaultBatchSize);
        var doc = fullDoc with { Lines = batches[0] };

        var docEntry = await _sales.CreateAsync(type, portalUsername, doc, ct);
        reportBatch(1);

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
            reportBatch(i + 1);
        }

        var created = await _sales.GetAsync(type, docEntry, ct);
        return (docEntry, created?.DocNum ?? 0, created?.DocTotal);
    }

    /// <summary>Documento de Venta completo (TODAS las líneas) de un grupo -- compartido por la creación real (que lo parte en lotes) y SimulateDocumentAsync.</summary>
    private async Task<SalesDocumentDto> BuildSalesDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        CancellationToken ct)
    {
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

        return new SalesDocumentDto(
            CustomerCardCode: cardCode,
            CustomerName: null,
            // Vendedor ya resuelto en el análisis según el modo del formato (fijo /
            // multivendedor / del socio) -- el mismo que se mostró en la vista previa.
            SalesEmployeeCode: first.SalesEmployeeCode ?? partnerDefaults?.SalesEmployeeCode,
            Comments: null,
            DocDate: DateOnly.FromDateTime(DateTime.Today),
            DocDueDate: DateOnly.FromDateTime(DateTime.Today),
            TaxDate: DateOnly.FromDateTime(DateTime.Today),
            CustomerReferenceNumber: first.CustomerReferenceNumber,
            Lines: lines,
            AdditionalFields: first.UserFieldsHeader.Count > 0 ? first.UserFieldsHeader : null,
            ShipToCode: first.Branch);
    }

    private async Task<(int DocEntry, int DocNum, decimal? DocTotal)> CreatePurchaseDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        string portalUsername, Action<int> reportBatch, CancellationToken ct)
    {
        var type = Enum.Parse<PurchaseDocumentType>(parameters.DocumentType);
        var fullDoc = BuildPurchaseDocument(parameters, document);
        var batches = Chunk(fullDoc.Lines.ToList(), DefaultBatchSize);
        var doc = fullDoc with { Lines = batches[0] };

        var docEntry = await _purchase.CreateAsync(type, portalUsername, doc, ct);
        reportBatch(1);

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
            reportBatch(i + 1);
        }

        var created = await _purchase.GetAsync(type, docEntry, ct);
        return (docEntry, created?.DocNum ?? 0, created?.DocTotal);
    }

    /// <summary>Documento de Compra completo (TODAS las líneas) de un grupo -- ver BuildSalesDocumentAsync.</summary>
    private static PurchaseDocumentDto BuildPurchaseDocument(GenericImportParametersDto parameters, GenericImportDocumentDto document)
    {
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

        return new PurchaseDocumentDto(
            SupplierCardCode: cardCode,
            SupplierName: null,
            Comments: null,
            DocDate: DateOnly.FromDateTime(DateTime.Today),
            DocDueDate: DateOnly.FromDateTime(DateTime.Today),
            TaxDate: DateOnly.FromDateTime(DateTime.Today),
            SupplierReferenceNumber: first.CustomerReferenceNumber,
            Lines: lines,
            AdditionalFields: first.UserFieldsHeader.Count > 0 ? first.UserFieldsHeader : null);
    }

    private async Task<(int DocEntry, int DocNum, decimal? DocTotal)> CreateInventoryDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        string portalUsername, Action<int> reportBatch, CancellationToken ct)
    {
        var type = Enum.Parse<InventoryDocumentType>(parameters.DocumentType);
        var fullDoc = BuildInventoryDocument(parameters, document);
        var batches = Chunk(fullDoc.Lines.ToList(), DefaultBatchSize);
        // Almacenes de cabecera: InventoryDocumentService los deriva de las líneas de este
        // primer lote (el más frecuente) -- en un traslado típico todas comparten
        // origen/destino, así que coincide con el documento completo.
        var doc = fullDoc with { Lines = batches[0] };

        var docEntry = await _inventory.CreateAsync(type, portalUsername, doc, ct);
        reportBatch(1);

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
            reportBatch(i + 1);
        }

        var created = await _inventory.GetAsync(type, docEntry, ct);
        return (docEntry, created?.DocNum ?? 0, null);
    }

    /// <summary>Documento de Inventario completo (TODAS las líneas) de un grupo -- ver BuildSalesDocumentAsync.</summary>
    private static InventoryDocumentDto BuildInventoryDocument(GenericImportParametersDto parameters, GenericImportDocumentDto document)
    {
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

        return new InventoryDocumentDto(
            DocDate: DateOnly.FromDateTime(DateTime.Today),
            Comments: null,
            Lines: lines,
            BusinessPartnerCardCode: string.IsNullOrWhiteSpace(cardCode) ? null : cardCode,
            CustomerReferenceNumber: first.CustomerReferenceNumber,
            AdditionalFields: first.UserFieldsHeader.Count > 0 ? first.UserFieldsHeader : null,
            // Sucursal -> "Destino" de la cabecera. Antes Branch se leía del Excel (y lo
            // validaba CustomerBranchValid) pero nunca se enviaba a SAP, que dejaba la
            // dirección por defecto del socio.
            ShipToCode: first.Branch);
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

    private void UpdateProgress(string jobId, string message, int current, int total, bool success, string? details, bool finished) =>
        _progress.Update(jobId, new GenericImportProgressDto(message, current, total, success, details, finished));

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

    /// <summary>Clave de crossReferenceBySocio para la traducción por código de barras (común a todos los socios) -- no puede chocar con un CardCode real.</summary>
    private const string BarcodeTranslationKey = "\u0000barcode";

    private static GenericImportRowDto ProcessRow(RawRow raw, GenericImportConfigDto config, GenericImportModule module,
        GenericImportLineType lineType, string? parameterBusinessPartnerCardCode, IReadOnlyDictionary<string, string> validBusinessPartners,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> crossReferenceBySocio, IReadOnlyDictionary<string, ItemDto> items,
        IReadOnlyDictionary<string, WarehouseDto> warehouses, IReadOnlyDictionary<string, GeneralLedgerAccountDto> accounts,
        IReadOnlyDictionary<string, CostCenterDto> costCenters, IReadOnlyDictionary<string, CostCenterDto> dimension2, IReadOnlyDictionary<string, CostCenterDto> dimension3,
        IReadOnlyDictionary<int, GenericImportUserFieldDto> userFieldsCatalog, IReadOnlyDictionary<string, decimal> systemPrices)
    {
        var errors = new List<string>();
        var issues = new List<GenericImportIssueDto>();

        // Todo error estructural lleva categoría -- decide en qué panel del análisis
        // aparece (ver GenericImportAnalysisBuilder). Errors (texto) se mantiene.
        void AddError(GenericImportIssueCategory category, string message)
        {
            errors.Add(message);
            issues.Add(new GenericImportIssueDto(category, true, message));
        }

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
                AddError(GenericImportIssueCategory.BusinessPartner, "El socio de negocio es obligatorio.");
            }
            else if (!validBusinessPartners.TryGetValue(rowCardCode, out businessPartnerName))
            {
                AddError(GenericImportIssueCategory.BusinessPartner, $"El socio de negocio \"{rowCardCode}\" no existe.");
            }
            else
            {
                businessPartnerCardCode = rowCardCode;
            }
        }

        var effectiveCardCode = businessPartnerCardCode ?? parameterBusinessPartnerCardCode ?? string.Empty;
        var itemTranslation = crossReferenceBySocio.GetValueOrDefault(
            config.ItemIdentifier == GenericImportItemIdentifier.Barcode ? BarcodeTranslationKey : effectiveCardCode, EmptyCrossReference);

        var quantity = ParseDecimal(Get(GenericImportLogicalField.Quantity));
        if (quantity is null)
        {
            AddError(GenericImportIssueCategory.Quantity, "Cantidad es obligatoria.");
        }

        var unitPrice = ParseDecimal(Get(GenericImportLogicalField.UnitPrice));
        var discountPercent = ParseDecimal(Get(GenericImportLogicalField.DiscountPercent)) ?? 0m;

        string? itemCode = null;
        string? itemName = null;

        // Columna de artículo del Excel -> ItemCode de SAP, según config.ItemIdentifier
        // (ItemCode directo, código de barras o SKU del socio). Común a Inventario y a
        // líneas de Artículo de Venta/Compra.
        void ResolveItem()
        {
            var excelCode = Get(GenericImportLogicalField.ItemCode)?.Trim();
            if (string.IsNullOrEmpty(excelCode))
            {
                AddError(GenericImportIssueCategory.Item, "CodigoArticulo es obligatorio.");
                return;
            }

            var resolvedCode = excelCode;
            var sourceLabel = config.ItemIdentifier == GenericImportItemIdentifier.Barcode ? "código de barras" : "SKU";
            if (config.ItemIdentifier != GenericImportItemIdentifier.ItemCode && !itemTranslation.TryGetValue(excelCode, out resolvedCode!))
            {
                AddError(GenericImportIssueCategory.Item, config.ItemIdentifier == GenericImportItemIdentifier.Barcode
                    ? $"El código de barras \"{excelCode}\" no corresponde a ningún artículo en SAP (o corresponde a más de uno)."
                    : $"El SKU \"{excelCode}\" no tiene paridad configurada para este socio de negocio.");
                return;
            }

            if (!items.TryGetValue(resolvedCode, out var item))
            {
                AddError(GenericImportIssueCategory.Item, config.ItemIdentifier == GenericImportItemIdentifier.ItemCode
                    ? $"El artículo \"{excelCode}\" no existe."
                    : $"El artículo \"{resolvedCode}\" ({sourceLabel} \"{excelCode}\") no existe en SAP.");
                return;
            }

            itemCode = item.ItemCode;
            itemName = item.ItemName;
        }

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
            ResolveItem();

            var sourceCode = Get(GenericImportLogicalField.SourceWarehouse)?.Trim();
            if (string.IsNullOrEmpty(sourceCode))
            {
                AddError(GenericImportIssueCategory.Warehouse, "AlmacenOrigen es obligatorio.");
            }
            else if (!warehouses.TryGetValue(sourceCode, out var sourceWarehouseDto))
            {
                AddError(GenericImportIssueCategory.Warehouse, $"El almacén de origen \"{sourceCode}\" no existe.");
            }
            else
            {
                sourceWarehouse = sourceWarehouseDto.WarehouseCode;
                sourceWarehouseName = sourceWarehouseDto.WarehouseName;
            }

            var destinationCode = Get(GenericImportLogicalField.DestinationWarehouse)?.Trim();
            if (string.IsNullOrEmpty(destinationCode))
            {
                AddError(GenericImportIssueCategory.Warehouse, "AlmacenDestino es obligatorio.");
            }
            else if (!warehouses.TryGetValue(destinationCode, out var destinationWarehouseDto))
            {
                AddError(GenericImportIssueCategory.Warehouse, $"El almacén de destino \"{destinationCode}\" no existe.");
            }
            else
            {
                destinationWarehouse = destinationWarehouseDto.WarehouseCode;
                destinationWarehouseName = destinationWarehouseDto.WarehouseName;
            }
        }
        else if (lineType == GenericImportLineType.Item)
        {
            ResolveItem();

            var warehouseCode = Get(GenericImportLogicalField.Warehouse)?.Trim();
            if (string.IsNullOrEmpty(warehouseCode))
            {
                AddError(GenericImportIssueCategory.Warehouse, "Bodega es obligatoria.");
            }
            else if (!warehouses.TryGetValue(warehouseCode, out var warehouseDto))
            {
                AddError(GenericImportIssueCategory.Warehouse, $"La bodega \"{warehouseCode}\" no existe.");
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
                AddError(GenericImportIssueCategory.Accounting, "Descripcion es obligatoria.");
            }

            var accountCode = Get(GenericImportLogicalField.Account)?.Trim();
            if (string.IsNullOrEmpty(accountCode))
            {
                AddError(GenericImportIssueCategory.Accounting, "CuentaMayor es obligatoria.");
            }
            else if (!accounts.TryGetValue(accountCode, out var accountDto))
            {
                AddError(GenericImportIssueCategory.Accounting, $"La cuenta mayor \"{accountCode}\" no existe.");
            }
            else
            {
                account = accountDto.AccountCode;
                accountName = accountDto.AccountName;
            }

            var costCenterCode = Get(GenericImportLogicalField.CostCenter)?.Trim();
            if (string.IsNullOrEmpty(costCenterCode))
            {
                AddError(GenericImportIssueCategory.Accounting, "CentroCostos es obligatorio.");
            }
            else if (!costCenters.TryGetValue(costCenterCode, out var costCenterDto))
            {
                AddError(GenericImportIssueCategory.Accounting, $"El centro de costos \"{costCenterCode}\" no existe.");
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
                    AddError(GenericImportIssueCategory.Accounting, $"Dimensión 2 \"{dim2Code}\" no existe.");
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
                    AddError(GenericImportIssueCategory.Accounting, $"Dimensión 3 \"{dim3Code}\" no existe.");
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
            // Socio EFECTIVO de la fila (el del archivo en multi-socio, si no el del
            // asistente/formato) -- antes solo se llenaba en multi-socio y las reglas
            // CustomerActiveInSap/CustomerBranchValid/PriceVsCustomerList, que filtran
            // por este campo, nunca validaban nada en la carga normal de un solo socio.
            BusinessPartnerCardCode = businessPartnerCardCode ?? (string.IsNullOrEmpty(effectiveCardCode) ? null : effectiveCardCode),
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

        // Las reglas estructurales (Cantidad > 0, Descuento 0-100) y las configurables
        // por Formato las corre GenericImportValidationRuleEngine sobre el set completo
        // de filas ya resueltas -- ver ProcessFileAsync.
        return row with { IsValid = errors.Count == 0, Errors = errors, Issues = issues };
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

    internal sealed class RawRow
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

    internal static List<RawRow> ReadRawRows(DataTable table, GenericImportConfigDto config)
    {
        var fieldsWithColumn = config.Fields.Where(f => f.LogicalField != GenericImportLogicalField.UserField).ToList();
        var userFields = config.Fields.Where(f => f.LogicalField == GenericImportLogicalField.UserField && f.UserFieldId is not null).ToList();

        // Una o varias columnas de agrupación ("A,B") -- la clave del grupo es la
        // combinación de sus valores, ver GroupingColumns.
        var groupingIndexes = GroupingColumns.Parse(config.GroupingColumn).Select(ColumnLetterToIndex).ToList();

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

            if (groupingIndexes.Count > 0)
            {
                var parts = groupingIndexes
                    .Select(index => index >= 0 && index < row.ItemArray.Length && row[index] != DBNull.Value ? row[index].ToString()!.Trim() : string.Empty)
                    .ToList();
                rawRow.RawGroupingKey = parts.All(string.IsNullOrEmpty) ? null : string.Join(GroupingColumns.KeySeparator, parts);
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
