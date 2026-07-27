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
    private readonly ISalesDocumentService _sales;
    private readonly IPurchaseDocumentService _purchase;
    private readonly IInventoryDocumentService _inventory;
    private readonly IGenericImportProgressStore _progress;
    private readonly ILogger<GenericImportService> _logger;

    public GenericImportService(IGenericImportConfigService config, IGenericImportUserFieldService userFieldsCatalog,
        IBusinessPartnerDefaultsService partnerDefaults, IItemCrossReferenceService crossReference, IItemCatalogService items,
        IWarehouseCatalogService warehouses, IGeneralLedgerAccountCatalogService accounts, ICostCenterCatalogService costCenters,
        IPriceListService priceList, ISalesDocumentService sales, IPurchaseDocumentService purchase, IInventoryDocumentService inventory,
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

        // Paridad SKU-socio -> ItemCode, solo si la configuración lo pide -- resuelto
        // una sola vez para todo el archivo, no por fila.
        var crossReferenceBySku = config.SkuIsCustomerOwn && !string.IsNullOrWhiteSpace(parameters.BusinessPartnerCardCode)
            ? (await _crossReference.ListAsync(parameters.BusinessPartnerCardCode, ct))
                .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
                .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().ItemCode, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> CodesOf(GenericImportLogicalField field) => rawRows
            .Select(r => r.CoreFields.GetValueOrDefault(field))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var excelItemCodes = CodesOf(GenericImportLogicalField.ItemCode);
        var itemCodesToResolve = config.SkuIsCustomerOwn
            ? excelItemCodes.Where(crossReferenceBySku.ContainsKey).Select(sku => crossReferenceBySku[sku]).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
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

        var rows = rawRows.Select(r => ProcessRow(r, config, parameters.Module, parameters.LineType, crossReferenceBySku, items,
            warehouses, accounts, costCenters, dimension2, dimension3, userFieldsCatalog, systemPrices)).ToList();

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

        UpdateProgress(jobId, $"Iniciando creación de {creatable.Count} documento(s)...", 0, creatable.Count, true, null, false);

        var partnerDefaults = !string.IsNullOrWhiteSpace(parameters.BusinessPartnerCardCode)
            ? await _partnerDefaults.GetAsync(parameters.BusinessPartnerCardCode, ct)
            : null;
        var results = new List<GenericImportDocumentResultDto>();
        var processed = 0;

        foreach (var document in creatable)
        {
            processed++;
            UpdateProgress(jobId, $"Creando documento {processed} de {creatable.Count} ({document.GroupingKey})...",
                processed - 1, creatable.Count, true, null, false);

            try
            {
                var docNum = parameters.Module switch
                {
                    GenericImportModule.Sales => await CreateSalesDocumentAsync(parameters, document, partnerDefaults, portalUsername, ct),
                    GenericImportModule.Purchase => await CreatePurchaseDocumentAsync(parameters, document, portalUsername, ct),
                    GenericImportModule.Inventory => await CreateInventoryDocumentAsync(parameters, document, portalUsername, ct),
                    _ => throw new InvalidOperationException($"Módulo {parameters.Module} no soportado."),
                };

                results.Add(new GenericImportDocumentResultDto(document.GroupingKey, true, docNum, "Documento creado correctamente."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear el documento del grupo {GroupingKey} en el trabajo de import {JobId}.", document.GroupingKey, jobId);
                results.Add(new GenericImportDocumentResultDto(document.GroupingKey, false, 0, ex.Message));
            }

            UpdateProgress(jobId, $"Procesado {processed} de {creatable.Count}...", processed, creatable.Count, true, null, false);
        }

        var successes = results.Count(r => r.Success);
        var final = new GenericImportProgressDto(
            $"Proceso completado: {successes} exitoso(s), {creatable.Count - successes} fallido(s).",
            creatable.Count, creatable.Count, successes == creatable.Count, null, true, results);
        _progress.Update(jobId, final);
        return final;
    }

    private async Task<int> CreateSalesDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        BusinessPartnerDefaultsDto? partnerDefaults, string portalUsername, CancellationToken ct)
    {
        var type = Enum.Parse<SalesDocumentType>(parameters.DocumentType);
        var first = document.Rows[0];

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

        var doc = new SalesDocumentDto(
            CustomerCardCode: parameters.BusinessPartnerCardCode ?? string.Empty,
            CustomerName: null,
            SalesEmployeeCode: partnerDefaults?.SalesEmployeeCode,
            Comments: null,
            DocDate: DateOnly.FromDateTime(DateTime.Today),
            DocDueDate: DateOnly.FromDateTime(DateTime.Today),
            TaxDate: DateOnly.FromDateTime(DateTime.Today),
            CustomerReferenceNumber: first.CustomerReferenceNumber,
            Lines: lines,
            AdditionalFields: first.UserFieldsHeader.Count > 0 ? first.UserFieldsHeader : null);

        var docEntry = await _sales.CreateAsync(type, portalUsername, doc, ct);
        var created = await _sales.GetAsync(type, docEntry, ct);
        return created?.DocNum ?? 0;
    }

    private async Task<int> CreatePurchaseDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        string portalUsername, CancellationToken ct)
    {
        var type = Enum.Parse<PurchaseDocumentType>(parameters.DocumentType);
        var first = document.Rows[0];

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

        var doc = new PurchaseDocumentDto(
            SupplierCardCode: parameters.BusinessPartnerCardCode ?? string.Empty,
            SupplierName: null,
            Comments: null,
            DocDate: DateOnly.FromDateTime(DateTime.Today),
            DocDueDate: DateOnly.FromDateTime(DateTime.Today),
            TaxDate: DateOnly.FromDateTime(DateTime.Today),
            SupplierReferenceNumber: first.CustomerReferenceNumber,
            Lines: lines,
            AdditionalFields: first.UserFieldsHeader.Count > 0 ? first.UserFieldsHeader : null);

        var docEntry = await _purchase.CreateAsync(type, portalUsername, doc, ct);
        var created = await _purchase.GetAsync(type, docEntry, ct);
        return created?.DocNum ?? 0;
    }

    private async Task<int> CreateInventoryDocumentAsync(GenericImportParametersDto parameters, GenericImportDocumentDto document,
        string portalUsername, CancellationToken ct)
    {
        var type = Enum.Parse<InventoryDocumentType>(parameters.DocumentType);
        var first = document.Rows[0];

        var lines = document.Rows.Select(r => new InventoryDocumentLineDto(
            ItemCode: r.ItemCode ?? string.Empty,
            Description: r.ItemName ?? r.ItemCode,
            Quantity: r.Quantity ?? 0,
            FromWarehouseCode: r.SourceWarehouse ?? string.Empty,
            ToWarehouseCode: r.DestinationWarehouse ?? string.Empty,
            AdditionalFields: r.UserFieldsLine.Count > 0 ? r.UserFieldsLine : null)).ToList();

        var doc = new InventoryDocumentDto(
            DocDate: DateOnly.FromDateTime(DateTime.Today),
            Comments: null,
            Lines: lines,
            AdditionalFields: first.UserFieldsHeader.Count > 0 ? first.UserFieldsHeader : null);

        var docEntry = await _inventory.CreateAsync(type, portalUsername, doc, ct);
        var created = await _inventory.GetAsync(type, docEntry, ct);
        return created?.DocNum ?? 0;
    }

    private void UpdateProgress(string jobId, string message, int current, int total, bool success, string? details, bool finished) =>
        _progress.Update(jobId, new GenericImportProgressDto(message, current, total, success, details, finished));

    // -------------------------------------------------------------------------------
    // Resolución fila a fila
    // -------------------------------------------------------------------------------

    private static GenericImportRowDto ProcessRow(RawRow raw, GenericImportConfigDto config, GenericImportModule module,
        GenericImportLineType lineType, IReadOnlyDictionary<string, string> crossReferenceBySku, IReadOnlyDictionary<string, ItemDto> items,
        IReadOnlyDictionary<string, WarehouseDto> warehouses, IReadOnlyDictionary<string, GeneralLedgerAccountDto> accounts,
        IReadOnlyDictionary<string, CostCenterDto> costCenters, IReadOnlyDictionary<string, CostCenterDto> dimension2, IReadOnlyDictionary<string, CostCenterDto> dimension3,
        IReadOnlyDictionary<int, GenericImportUserFieldDto> userFieldsCatalog, IReadOnlyDictionary<string, decimal> systemPrices)
    {
        var errors = new List<string>();

        string? Get(GenericImportLogicalField field) => raw.CoreFields.GetValueOrDefault(field);

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

        var groupingKey = string.IsNullOrEmpty(config.GroupingColumn) ? SingleGroupingKey : (raw.RawGroupingKey ?? SingleGroupingKey);

        var row = new GenericImportRowDto
        {
            RowNumber = raw.RowNumber,
            GroupingKey = groupingKey,
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
