using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using PortalSaas.Core.Sap;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

public sealed class GenericImportFormatValidatorTests
{
    private static GenericImportConfigDto Config(GenericImportModule module, string documentType, GenericImportLineType lineType,
        params GenericImportConfigFieldDto[] fields) =>
        new(1, Guid.NewGuid(), module, documentType, lineType, null, null, false, "Prueba", true, fields);

    private static GenericImportConfigFieldDto Field(GenericImportLogicalField field, string? column, string? fixedValue = null) =>
        new(0, field, column, false, fixedValue, null);

    private static List<GenericImportFormatCheckDto> CoreChecks(GenericImportConfigDto config)
    {
        var checks = new List<GenericImportFormatCheckDto>();
        GenericImportFormatValidator.CheckCoreFields(config, checks);
        return checks;
    }

    [Fact]
    public void Inventario_sin_almacen_destino_es_error_y_Almacen_mapeado_se_advierte_como_ignorado()
    {
        // Caso real: formato de Solicitud de traslado con "Warehouse" mapeado (columna D),
        // que Inventario ignora -- usa Almacén origen/destino.
        var config = Config(GenericImportModule.Inventory, "InventoryTransferRequest", GenericImportLineType.Item,
            Field(GenericImportLogicalField.Quantity, "E"),
            Field(GenericImportLogicalField.ItemCode, "C"),
            Field(GenericImportLogicalField.SourceWarehouse, null, "C_RETAIL"),
            Field(GenericImportLogicalField.Warehouse, "D"));

        var checks = CoreChecks(config);

        Assert.Contains(checks, c => c.Severity == GenericImportFormatCheckSeverity.Error && c.Message.StartsWith("DestinationWarehouse"));
        Assert.Contains(checks, c => c.Severity == GenericImportFormatCheckSeverity.Warning && c.Message.StartsWith("Warehouse está mapeado pero se ignora"));
        Assert.DoesNotContain(checks, c => c.Severity == GenericImportFormatCheckSeverity.Error && c.Message.StartsWith("SourceWarehouse"));
    }

    [Fact]
    public void Columna_y_valor_fijo_a_la_vez_avisa_que_manda_el_fijo()
    {
        var config = Config(GenericImportModule.Sales, "SalesOrder", GenericImportLineType.Item,
            Field(GenericImportLogicalField.Quantity, "B"),
            Field(GenericImportLogicalField.ItemCode, "A"),
            Field(GenericImportLogicalField.Warehouse, "D", "C_RETAIL"));

        var checks = CoreChecks(config);

        Assert.Contains(checks, c => c.Severity == GenericImportFormatCheckSeverity.Info && c.Message.Contains("manda el valor fijo"));
    }

    [Fact]
    public void Letra_de_columna_invalida_y_columna_repetida()
    {
        var config = Config(GenericImportModule.Sales, "SalesOrder", GenericImportLineType.Item,
            Field(GenericImportLogicalField.Quantity, "B"),
            Field(GenericImportLogicalField.ItemCode, "B"),
            Field(GenericImportLogicalField.Warehouse, "1"));

        var checks = CoreChecks(config);

        Assert.Contains(checks, c => c.Severity == GenericImportFormatCheckSeverity.Error && c.Message.Contains("\"1\" no es una letra"));
        Assert.Contains(checks, c => c.Severity == GenericImportFormatCheckSeverity.Warning && c.Message.StartsWith("La columna B está mapeada a varios campos"));
    }

    [Fact]
    public void Servicio_exige_descripcion_cuenta_y_centro_de_costos()
    {
        var config = Config(GenericImportModule.Purchase, "PurchaseOrder", GenericImportLineType.Service,
            Field(GenericImportLogicalField.Quantity, "A"));

        var errores = CoreChecks(config).Where(c => c.Severity == GenericImportFormatCheckSeverity.Error).Select(c => c.Message).ToList();

        Assert.Contains(errores, m => m.StartsWith("Description"));
        Assert.Contains(errores, m => m.StartsWith("Account"));
        Assert.Contains(errores, m => m.StartsWith("CostCenter"));
    }

    [Theory]
    [InlineData(GenericImportModule.Inventory, "InventoryTransferRequest", "OWTQ", "WTQ1")]
    [InlineData(GenericImportModule.Sales, "SalesOrder", "ORDR", "RDR1")]
    [InlineData(GenericImportModule.Purchase, "PurchaseQuotation", "OPQT", "PQT1")]
    public void Tablas_de_cabecera_y_linea_del_documento(GenericImportModule module, string documentType, string header, string line)
    {
        var tables = GenericImportFormatValidator.DocumentTables(Config(module, documentType, GenericImportLineType.Item));

        Assert.Equal((header, line), tables);
    }

    [Theory]
    [InlineData(GenericImportFieldDataType.Text, "A", true)]
    [InlineData(GenericImportFieldDataType.Text, "M", true)]
    [InlineData(GenericImportFieldDataType.Number, "B", true)]
    [InlineData(GenericImportFieldDataType.Date, "D", true)]
    [InlineData(GenericImportFieldDataType.Number, "A", false)]
    [InlineData(GenericImportFieldDataType.Date, "N", false)]
    public void Tipo_de_dato_contra_CUFD(GenericImportFieldDataType declared, string sapType, bool compatible)
    {
        Assert.Equal(compatible, GenericImportFormatValidator.DataTypeMismatch(declared, sapType) is null);
    }

    // ------------------------------------------------ interpretación del envío simulado

    private const string SentinelError = """{"error":{"code":-2028,"message":{"lang":"en-us","value":"No matching records found (ODBC -2028)"}}}""";
    private const string DocumentError = """{"error":{"code":-5002,"message":{"lang":"en-us","value":"Property 'Usuario-Numero Orden ' of 'StockTransfer' is invalid"}}}""";

    [Fact]
    public void Falla_el_centinela_con_content_id_2_es_valido()
    {
        var (outcome, _) = SapSimulationResponseInterpreter.Interpret([new(404, "2", SentinelError)]);
        Assert.Equal(SapSimulationOutcome.Valid, outcome);
    }

    [Fact]
    public void Falla_el_documento_es_rechazado_con_el_mensaje_de_SAP()
    {
        var (outcome, message) = SapSimulationResponseInterpreter.Interpret([new(400, "1", DocumentError)]);
        Assert.Equal(SapSimulationOutcome.Rejected, outcome);
        Assert.Equal("Property 'Usuario-Numero Orden ' of 'StockTransfer' is invalid", message);
    }

    [Fact]
    public void Sin_content_id_se_distingue_por_el_codigo_del_centinela()
    {
        Assert.Equal(SapSimulationOutcome.Valid, SapSimulationResponseInterpreter.Interpret([new(404, null, SentinelError)]).Outcome);
        Assert.Equal(SapSimulationOutcome.Rejected, SapSimulationResponseInterpreter.Interpret([new(400, null, DocumentError)]).Outcome);
    }

    [Fact]
    public void Documento_ok_y_centinela_fallido_en_partes_separadas_es_valido()
    {
        var (outcome, _) = SapSimulationResponseInterpreter.Interpret([new(204, null, ""), new(404, null, SentinelError)]);
        Assert.Equal(SapSimulationOutcome.Valid, outcome);
    }

    [Fact]
    public void Todo_exitoso_es_inconcluso_y_avisa_revisar_SAP()
    {
        var (outcome, message) = SapSimulationResponseInterpreter.Interpret([new(204, null, ""), new(204, null, "")]);
        Assert.Equal(SapSimulationOutcome.Inconclusive, outcome);
        Assert.Contains("Revisá en SAP", message);
    }
}
