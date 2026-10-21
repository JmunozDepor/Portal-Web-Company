using System.Text.RegularExpressions;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Compras;
using PortalSaas.Core.Inventario;
using PortalSaas.Core.Ventas;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// "Probar formato" de Importación Genérica (Configuración) -- valida un formato contra el
/// SAP de la compañía activa SIN crear documentos. Ver IGenericImportFormatValidator.
///
/// Las reglas de "obligatorio"/"se ignora" por módulo y tipo de línea reflejan
/// GenericImportService.ProcessRow y los Build*Document -- si esos cambian, actualizar
/// RequiredFields/IgnoredFields acá.
/// </summary>
public sealed class GenericImportFormatValidator : IGenericImportFormatValidator
{
    private const string AreaCore = "Campos núcleo";
    private const string AreaUserFields = "Campos de usuario";
    private const string AreaFixedValues = "Valores fijos";
    private const string AreaFile = "Archivo de prueba";
    private const string AreaSimulation = "Envío simulado";

    private static readonly Regex ColumnPattern = new("^[A-Za-z]{1,3}$", RegexOptions.Compiled);

    private readonly IGenericImportConfigService _configs;
    private readonly IGenericImportUserFieldService _userFields;
    private readonly IGenericImportService _importService;
    private readonly IHanaService _hana;

    public GenericImportFormatValidator(IGenericImportConfigService configs, IGenericImportUserFieldService userFields,
        IGenericImportService importService, IHanaService hana)
    {
        _configs = configs;
        _userFields = userFields;
        _importService = importService;
        _hana = hana;
    }

    public async Task<IReadOnlyList<GenericImportFormatCheckDto>> ValidateSchemaAsync(int configId, CancellationToken ct = default)
    {
        var config = await _configs.GetAsync(configId, ct)
            ?? throw new InvalidOperationException("Formato no encontrado.");

        var checks = new List<GenericImportFormatCheckDto>();
        CheckCoreFields(config, checks);
        await CheckFixedValuesAsync(config, checks, ct);
        await CheckUserFieldsAsync(config, checks, ct);
        await CheckDocumentOptionsAsync(config, checks, ct);
        return checks;
    }

    public async Task<GenericImportFormatTestResultDto> TestWithFileAsync(int configId, Stream file, string portalUsername,
        string? businessPartnerCardCode, CancellationToken ct = default)
    {
        var config = await _configs.GetAsync(configId, ct)
            ?? throw new InvalidOperationException("Formato no encontrado.");

        var checks = (await ValidateSchemaAsync(configId, ct)).ToList();

        var cardCode = config.BusinessPartnerCardCode
            ?? (string.IsNullOrWhiteSpace(businessPartnerCardCode) ? null : businessPartnerCardCode.Trim());
        var parameters = new GenericImportParametersDto(config.Module, config.DocumentType, config.LineType, cardCode, config.Id);

        var preview = await _importService.ProcessFileAsync(parameters, file, ct);
        if (!preview.HasValidConfig)
        {
            checks.Add(new(GenericImportFormatCheckSeverity.Error, AreaFile, preview.ErrorMessage ?? "El archivo no se pudo procesar con este formato."));
            return new GenericImportFormatTestResultDto { Checks = checks, Preview = preview };
        }

        var rows = preview.Documents.SelectMany(d => d.Rows).ToList();
        var rowsWithErrors = rows.Count(r => !r.IsValid);
        checks.Add(new(rowsWithErrors == 0 ? GenericImportFormatCheckSeverity.Ok : GenericImportFormatCheckSeverity.Warning, AreaFile,
            $"{preview.Documents.Count} documento(s), {rows.Count} fila(s) leída(s), {rowsWithErrors} con error."));

        var documentToSimulate = preview.Documents.FirstOrDefault(d => d.CanCreate);
        if (documentToSimulate is null)
        {
            checks.Add(new(GenericImportFormatCheckSeverity.Warning, AreaSimulation,
                "No se simuló el envío: ningún documento quedó sin errores. Corregí las filas marcadas en la vista previa y volvé a probar."));
            return new GenericImportFormatTestResultDto { Checks = checks, Preview = preview };
        }

        if (config.Module != GenericImportModule.Inventory && !config.BusinessPartnerFromFile && cardCode is null)
        {
            checks.Add(new(GenericImportFormatCheckSeverity.Warning, AreaSimulation,
                "Formato estándar sin socio: indicá un socio de prueba para simular el envío (Venta/Compra lo exigen)."));
            return new GenericImportFormatTestResultDto { Checks = checks, Preview = preview };
        }

        var simulation = await _importService.SimulateDocumentAsync(parameters, documentToSimulate, portalUsername, ct);
        checks.Add(new(simulation.Outcome switch
        {
            SapSimulationOutcome.Valid => GenericImportFormatCheckSeverity.Ok,
            SapSimulationOutcome.Rejected => GenericImportFormatCheckSeverity.Error,
            _ => GenericImportFormatCheckSeverity.Warning,
        }, AreaSimulation, $"Grupo \"{documentToSimulate.GroupingKey}\": {simulation.Message}"));

        return new GenericImportFormatTestResultDto
        {
            Checks = checks,
            Preview = preview,
            SimulatedGroupingKey = documentToSimulate.GroupingKey,
            Simulation = simulation,
        };
    }

    // ------------------------------------------------------------------ campos núcleo

    internal static IReadOnlyCollection<GenericImportLogicalField> RequiredFields(GenericImportConfigDto config)
    {
        var required = new List<GenericImportLogicalField> { GenericImportLogicalField.Quantity };
        if (config.Module == GenericImportModule.Inventory)
        {
            required.AddRange([GenericImportLogicalField.ItemCode, GenericImportLogicalField.SourceWarehouse, GenericImportLogicalField.DestinationWarehouse]);
        }
        else if (config.LineType == GenericImportLineType.Item)
        {
            required.Add(GenericImportLogicalField.ItemCode);
            // Con almacén del cliente (campo de usuario del socio) la columna/valor fijo es
            // solo respaldo -- no obligatoria. Con almacén "en pantalla" no se mapea.
            if (config.DocumentOptions.WarehouseSource == GenericImportWarehouseSource.Mapping)
            {
                required.Add(GenericImportLogicalField.Warehouse);
            }
        }
        else
        {
            required.AddRange([GenericImportLogicalField.Description, GenericImportLogicalField.Account, GenericImportLogicalField.CostCenter]);
        }

        if (config.BusinessPartnerFromFile)
        {
            required.Add(GenericImportLogicalField.BusinessPartnerCardCode);
        }

        return required;
    }

    /// <summary>Campo núcleo -> motivo por el que el módulo/tipo de línea lo ignora (se lee del Excel pero no llega a SAP).</summary>
    internal static IReadOnlyDictionary<GenericImportLogicalField, string> IgnoredFields(GenericImportConfigDto config)
    {
        var ignored = new Dictionary<GenericImportLogicalField, string>();

        void Ignore(string reason, params GenericImportLogicalField[] fields)
        {
            foreach (var field in fields)
            {
                ignored[field] = reason;
            }
        }

        if (config.Module == GenericImportModule.Inventory)
        {
            Ignore("Inventario usa Almacén origen/destino, no Almacén", GenericImportLogicalField.Warehouse);
            Ignore("Inventario no lleva precio ni descuento", GenericImportLogicalField.UnitPrice, GenericImportLogicalField.DiscountPercent);
            Ignore("Inventario solo traslada artículos (no hay líneas de servicio)", GenericImportLogicalField.Description,
                GenericImportLogicalField.Account, GenericImportLogicalField.CostCenter, GenericImportLogicalField.Dimension2, GenericImportLogicalField.Dimension3);
        }
        else
        {
            Ignore("Almacén origen/destino solo aplica a Inventario", GenericImportLogicalField.SourceWarehouse, GenericImportLogicalField.DestinationWarehouse);
            if (config.LineType == GenericImportLineType.Item)
            {
                Ignore("en líneas de Artículo la descripción sale del maestro de artículos", GenericImportLogicalField.Description);
                Ignore("Cuenta/Centro de costos/Dimensiones solo aplican a líneas de Servicio", GenericImportLogicalField.Account,
                    GenericImportLogicalField.CostCenter, GenericImportLogicalField.Dimension2, GenericImportLogicalField.Dimension3);
            }
            else
            {
                Ignore("Artículo/Almacén solo aplican a líneas de Artículo", GenericImportLogicalField.ItemCode, GenericImportLogicalField.Warehouse);
            }
        }

        if (config.Module == GenericImportModule.Purchase)
        {
            Ignore("en Compras la sucursal (ShipToCode) es la dirección propia, no la del proveedor -- no se envía", GenericImportLogicalField.Branch);
        }

        if (!config.BusinessPartnerFromFile)
        {
            Ignore("el socio viene del formato o del asistente; activá \"Carga multi-socio\" para leerlo por fila", GenericImportLogicalField.BusinessPartnerCardCode);
        }

        if (config.DocumentOptions.BranchSource == GenericImportBranchSource.Screen && config.Module != GenericImportModule.Purchase)
        {
            Ignore("la sucursal se pide en pantalla al importar", GenericImportLogicalField.Branch);
        }

        if (config.DocumentOptions.WarehouseSource == GenericImportWarehouseSource.Screen && config.Module != GenericImportModule.Inventory)
        {
            Ignore("el almacén se pide en pantalla al importar", GenericImportLogicalField.Warehouse);
        }

        return ignored;
    }

    internal static void CheckCoreFields(GenericImportConfigDto config, List<GenericImportFormatCheckDto> checks)
    {
        var coreFields = config.Fields.Where(f => f.LogicalField != GenericImportLogicalField.UserField).ToList();
        static bool HasColumn(GenericImportConfigFieldDto f) => !string.IsNullOrWhiteSpace(f.ExcelColumn);
        static bool HasFixed(GenericImportConfigFieldDto f) => !string.IsNullOrWhiteSpace(f.FixedValue);
        var mapped = coreFields.Where(f => HasColumn(f) || HasFixed(f)).ToDictionary(f => f.LogicalField);

        foreach (var field in RequiredFields(config))
        {
            checks.Add(mapped.ContainsKey(field)
                ? new(GenericImportFormatCheckSeverity.Ok, AreaCore, $"{field}: mapeado.")
                : new(GenericImportFormatCheckSeverity.Error, AreaCore, $"{field} es obligatorio para {config.Module}/{config.LineType} y no tiene columna ni valor fijo."));
        }

        var ignored = IgnoredFields(config);
        foreach (var (field, mapping) in mapped)
        {
            if (ignored.TryGetValue(field, out var reason))
            {
                checks.Add(new(GenericImportFormatCheckSeverity.Warning, AreaCore, $"{field} está mapeado pero se ignora: {reason}."));
            }

            if (HasColumn(mapping) && HasFixed(mapping))
            {
                checks.Add(new(GenericImportFormatCheckSeverity.Info, AreaCore,
                    $"{field}: tiene columna {mapping.ExcelColumn!.Trim().ToUpperInvariant()} y valor fijo \"{mapping.FixedValue!.Trim()}\" -- manda el valor fijo, la columna no se lee."));
            }
        }

        // Letras de columna -- campos núcleo + campos de usuario + columna de agrupación.
        var columns = config.Fields.Where(HasColumn)
            .Select(f => (Name: f.LogicalField == GenericImportLogicalField.UserField ? $"Campo de usuario #{f.UserFieldId}" : f.LogicalField.ToString(),
                Column: f.ExcelColumn!.Trim().ToUpperInvariant()))
            .ToList();
        foreach (var (name, column) in columns.Where(c => !ColumnPattern.IsMatch(c.Column)))
        {
            checks.Add(new(GenericImportFormatCheckSeverity.Error, AreaCore, $"{name}: \"{column}\" no es una letra de columna Excel válida (ej. A, B, AA)."));
        }

        foreach (var group in columns.Where(c => ColumnPattern.IsMatch(c.Column)).GroupBy(c => c.Column).Where(g => g.Count() > 1))
        {
            checks.Add(new(GenericImportFormatCheckSeverity.Warning, AreaCore,
                $"La columna {group.Key} está mapeada a varios campos: {string.Join(", ", group.Select(c => c.Name))}."));
        }

        foreach (var groupingColumn in GroupingColumns.Parse(config.GroupingColumn).Where(c => !ColumnPattern.IsMatch(c)))
        {
            checks.Add(new(GenericImportFormatCheckSeverity.Error, AreaCore, $"Columna de agrupación \"{groupingColumn}\" no es una letra de columna Excel válida."));
        }
    }

    // ------------------------------------------------------------------ valores fijos

    private async Task CheckFixedValuesAsync(GenericImportConfigDto config, List<GenericImportFormatCheckDto> checks, CancellationToken ct)
    {
        var ignored = IgnoredFields(config);
        var cardType = config.Module == GenericImportModule.Purchase ? "S" : "C";

        if (!string.IsNullOrWhiteSpace(config.BusinessPartnerCardCode))
        {
            var exists = await ExistsAsync("""SELECT "CardCode" FROM "OCRD" WHERE "CardCode" = :code AND "CardType" = :cardType""",
                new { code = config.BusinessPartnerCardCode.Trim(), cardType }, ct);
            checks.Add(exists
                ? new(GenericImportFormatCheckSeverity.Ok, AreaFixedValues, $"Socio del formato \"{config.BusinessPartnerCardCode}\": existe en SAP.")
                : new(GenericImportFormatCheckSeverity.Error, AreaFixedValues,
                    $"Socio del formato \"{config.BusinessPartnerCardCode}\": no existe en SAP como {(cardType == "S" ? "proveedor" : "cliente")}."));
        }

        foreach (var field in config.Fields.Where(f => f.LogicalField != GenericImportLogicalField.UserField
                     && !string.IsNullOrWhiteSpace(f.FixedValue) && !ignored.ContainsKey(f.LogicalField)))
        {
            var value = field.FixedValue!.Trim();
            var (sql, label, parameters) = field.LogicalField switch
            {
                GenericImportLogicalField.Warehouse or GenericImportLogicalField.SourceWarehouse or GenericImportLogicalField.DestinationWarehouse =>
                    ("""SELECT "WhsCode" FROM "OWHS" WHERE "WhsCode" = :code""", "almacén", (object)new { code = value }),
                GenericImportLogicalField.ItemCode when config.ItemIdentifier == GenericImportItemIdentifier.ItemCode =>
                    ("""SELECT "ItemCode" FROM "OITM" WHERE "ItemCode" = :code""", "artículo", new { code = value }),
                GenericImportLogicalField.Account =>
                    ("""SELECT "AcctCode" FROM "OACT" WHERE "AcctCode" = :code""", "cuenta mayor", new { code = value }),
                GenericImportLogicalField.CostCenter or GenericImportLogicalField.Dimension2 or GenericImportLogicalField.Dimension3 =>
                    ("""SELECT "PrcCode" FROM "OPRC" WHERE "PrcCode" = :code""", "centro de costos", new { code = value }),
                GenericImportLogicalField.BusinessPartnerCardCode =>
                    ("""SELECT "CardCode" FROM "OCRD" WHERE "CardCode" = :code AND "CardType" = :cardType""", "socio de negocio", new { code = value, cardType }),
                GenericImportLogicalField.Branch when !string.IsNullOrWhiteSpace(config.BusinessPartnerCardCode) =>
                    ("""SELECT "Address" FROM "CRD1" WHERE "CardCode" = :cardCode AND "AdresType" = 'S' AND "Address" = :code""",
                        "sucursal (dirección de despacho del socio)", new { code = value, cardCode = config.BusinessPartnerCardCode.Trim() }),
                _ => (null, null, null),
            };

            if (sql is null)
            {
                continue;
            }

            var exists = await ExistsAsync(sql, parameters, ct);
            checks.Add(exists
                ? new(GenericImportFormatCheckSeverity.Ok, AreaFixedValues, $"{field.LogicalField} = \"{value}\": {label} existe en SAP.")
                : new(GenericImportFormatCheckSeverity.Error, AreaFixedValues, $"{field.LogicalField} = \"{value}\": el {label} no existe en SAP."));
        }
    }

    /// <summary>Almacén del cliente: el campo de usuario existe en OCRD. Vendedor fijo: existe en OSLP.</summary>
    private async Task CheckDocumentOptionsAsync(GenericImportConfigDto config, List<GenericImportFormatCheckDto> checks, CancellationToken ct)
    {
        var options = config.DocumentOptions;
        // También en modo "pantalla": el campo precarga el selector de almacén de Importar.
        if (options.WarehouseSource is GenericImportWarehouseSource.BusinessPartnerField or GenericImportWarehouseSource.Screen
            && config.Module != GenericImportModule.Inventory)
        {
            var userField = options.WarehouseUserField ?? GenericImportDocumentOptionsDto.DefaultWarehouseUserField;
            var exists = userField.StartsWith("U_", StringComparison.OrdinalIgnoreCase)
                && await ExistsAsync("""SELECT "AliasID" FROM "CUFD" WHERE "TableID" = 'OCRD' AND "AliasID" = :alias""", new { alias = userField[2..] }, ct);
            checks.Add(exists
                ? new(GenericImportFormatCheckSeverity.Ok, AreaFixedValues, $"Almacén del cliente: el campo {userField} existe en el maestro de socios (OCRD).")
                : new(GenericImportFormatCheckSeverity.Error, AreaFixedValues, $"Almacén del cliente: el campo {userField} no existe en el maestro de socios (OCRD)."));
        }

        if (config.Module == GenericImportModule.Sales && options.SalesPersonMode == GenericImportSalesPersonMode.Fixed)
        {
            var exists = options.SalesPersonCode is { } slp
                && await ExistsAsync("""SELECT "SlpCode" FROM "OSLP" WHERE "SlpCode" = :slp""", new { slp }, ct);
            checks.Add(exists
                ? new(GenericImportFormatCheckSeverity.Ok, AreaFixedValues, $"Vendedor fijo {options.SalesPersonCode}: existe en SAP.")
                : new(GenericImportFormatCheckSeverity.Error, AreaFixedValues, $"Vendedor fijo {options.SalesPersonCode}: no existe en SAP."));
        }
    }

    private async Task<bool> ExistsAsync(string sql, object? parameters, CancellationToken ct) =>
        (await _hana.QueryDynamicAsync(sql, parameters, ct)).Count > 0;

    // ------------------------------------------------------------------ campos de usuario

    private async Task CheckUserFieldsAsync(GenericImportConfigDto config, List<GenericImportFormatCheckDto> checks, CancellationToken ct)
    {
        var mappedUserFields = config.Fields
            .Where(f => f.LogicalField == GenericImportLogicalField.UserField && f.UserFieldId is not null
                && (!string.IsNullOrWhiteSpace(f.ExcelColumn) || !string.IsNullOrWhiteSpace(f.FixedValue)))
            .ToList();
        if (mappedUserFields.Count == 0)
        {
            return;
        }

        var catalog = (await _userFields.ListAsync(config.Module, ct)).ToDictionary(f => f.Id);
        var (headerTable, lineTable) = DocumentTables(config);

        var udfRows = await _hana.QueryDynamicAsync(
            """SELECT "TableID", "AliasID", "TypeID" FROM "CUFD" WHERE "TableID" IN (:headerTable, :lineTable)""",
            new { headerTable, lineTable }, ct);
        var udfs = udfRows
            .Select(r => (Table: r["TableID"]?.ToString()?.Trim() ?? string.Empty,
                Alias: r["AliasID"]?.ToString()?.Trim() ?? string.Empty,
                Type: r["TypeID"]?.ToString()?.Trim() ?? string.Empty))
            .ToList();

        foreach (var mapping in mappedUserFields)
        {
            if (!catalog.TryGetValue(mapping.UserFieldId!.Value, out var field))
            {
                checks.Add(new(GenericImportFormatCheckSeverity.Error, AreaUserFields,
                    $"Campo de usuario #{mapping.UserFieldId} no existe (o no es de {config.Module}) en el catálogo de Campos de usuario."));
                continue;
            }

            var name = $"\"{field.Label}\" ({field.SapFieldName})";
            if (!field.IsActive)
            {
                checks.Add(new(GenericImportFormatCheckSeverity.Warning, AreaUserFields, $"{name}: está inactivo en el catálogo de Campos de usuario."));
            }

            if (!field.SapFieldName.StartsWith("U_", StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(new(GenericImportFormatCheckSeverity.Info, AreaUserFields,
                    $"{name}: campo nativo de Service Layer -- no se puede verificar sin enviar; usá \"Probar con archivo\" (envío simulado)."));
                continue;
            }

            var alias = field.SapFieldName[2..];
            var expectedTable = field.Level == GenericImportFieldLevel.Header ? headerTable : lineTable;
            var otherTable = field.Level == GenericImportFieldLevel.Header ? lineTable : headerTable;
            var match = udfs.FirstOrDefault(u => u.Table.Equals(expectedTable, StringComparison.OrdinalIgnoreCase)
                && u.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));

            if (match.Table is null || match.Alias.Length == 0)
            {
                var inOther = udfs.Any(u => u.Table.Equals(otherTable, StringComparison.OrdinalIgnoreCase)
                    && u.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));
                checks.Add(new(GenericImportFormatCheckSeverity.Error, AreaUserFields, inOther
                    ? $"{name}: existe en {otherTable} ({(field.Level == GenericImportFieldLevel.Header ? "línea" : "cabecera")}), no en {expectedTable} -- revisá el Nivel del campo."
                    : $"{name}: no existe en SAP en la tabla {expectedTable} ({(field.Level == GenericImportFieldLevel.Header ? "cabecera" : "línea")})."));
                continue;
            }

            var typeWarning = DataTypeMismatch(field.DataType, match.Type);
            checks.Add(typeWarning is null
                ? new(GenericImportFormatCheckSeverity.Ok, AreaUserFields, $"{name}: existe en {expectedTable}.")
                : new(GenericImportFormatCheckSeverity.Warning, AreaUserFields, $"{name}: existe en {expectedTable}, pero {typeWarning}."));
        }
    }

    /// <summary>Tabla de cabecera y de línea del tipo de documento (convención SAP: ORDR -> RDR1, OWTQ -> WTQ1).</summary>
    internal static (string Header, string Line) DocumentTables(GenericImportConfigDto config)
    {
        var header = config.Module switch
        {
            GenericImportModule.Sales => SalesDocumentTypeCatalog.Resolve(Enum.Parse<SalesDocumentType>(config.DocumentType)).Table,
            GenericImportModule.Purchase => PurchaseDocumentTypeCatalog.Resolve(Enum.Parse<PurchaseDocumentType>(config.DocumentType)).Table,
            GenericImportModule.Inventory => InventoryDocumentTypeCatalog.Resolve(Enum.Parse<InventoryDocumentType>(config.DocumentType)).Table,
            _ => throw new InvalidOperationException("Módulo no soportado."),
        };
        return (header, header[1..] + "1");
    }

    /// <summary>CUFD.TypeID: A = alfanumérico, M = texto/memo, N = numérico, B = unidades/importe, D = fecha/hora.</summary>
    internal static string? DataTypeMismatch(GenericImportFieldDataType declared, string sapTypeId)
    {
        var compatible = declared switch
        {
            GenericImportFieldDataType.Text => sapTypeId is "A" or "M",
            GenericImportFieldDataType.Number => sapTypeId is "N" or "B",
            GenericImportFieldDataType.Date => sapTypeId is "D",
            _ => true,
        };
        if (compatible)
        {
            return null;
        }

        var sapType = sapTypeId switch
        {
            "A" => "alfanumérico",
            "M" => "texto",
            "N" => "numérico",
            "B" => "importe/unidades",
            "D" => "fecha",
            _ => sapTypeId,
        };
        return $"en SAP es de tipo {sapType} y en el catálogo está declarado como {declared}";
    }
}
