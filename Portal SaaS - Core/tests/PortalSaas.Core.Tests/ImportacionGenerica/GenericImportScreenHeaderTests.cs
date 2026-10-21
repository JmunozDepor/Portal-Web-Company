using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using PortalSaas.Data;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

/// <summary>Formato genérico "solo detalle" (2026-10-21): sucursal y almacén pedidos en la cabecera de Importar.</summary>
public sealed class GenericImportScreenHeaderTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static GenericImportConfigDto Config(GenericImportModule module = GenericImportModule.Sales,
        GenericImportBranchSource branch = GenericImportBranchSource.Screen,
        GenericImportWarehouseSource warehouse = GenericImportWarehouseSource.Screen) =>
        new(1, CompanyId, module, "SalesOrder", GenericImportLineType.Item, null, null, GenericImportItemIdentifier.ItemCode,
            "Estándar solo detalle", true, [])
        {
            DocumentOptions = new GenericImportDocumentOptionsDto(WarehouseSource: warehouse, BranchSource: branch),
        };

    private static List<GenericImportService.RawRow> Rows()
    {
        var first = new GenericImportService.RawRow { RowNumber = 1 };
        first.CoreFields[GenericImportLogicalField.Branch] = "DEL-EXCEL";
        first.CoreFields[GenericImportLogicalField.Warehouse] = "99";
        var second = new GenericImportService.RawRow { RowNumber = 2 };
        return [first, second];
    }

    [Fact]
    public void Sucursal_y_almacen_en_pantalla_pisan_todas_las_filas()
    {
        var rows = Rows();
        var parameters = new GenericImportParametersDto(GenericImportModule.Sales, "SalesOrder", GenericImportLineType.Item, "C1",
            ShipToCode: " SUC-01 ", WarehouseCode: "01");

        GenericImportService.ApplyScreenValues(rows, Config(), parameters);

        Assert.All(rows, r =>
        {
            Assert.Equal("SUC-01", r.CoreFields[GenericImportLogicalField.Branch]);
            Assert.Equal("01", r.CoreFields[GenericImportLogicalField.Warehouse]);
        });
    }

    [Fact]
    public void Formato_que_lee_del_excel_no_toca_las_filas()
    {
        var rows = Rows();
        var parameters = new GenericImportParametersDto(GenericImportModule.Sales, "SalesOrder", GenericImportLineType.Item, "C1",
            ShipToCode: "SUC-01", WarehouseCode: "01");

        GenericImportService.ApplyScreenValues(rows,
            Config(branch: GenericImportBranchSource.Mapping, warehouse: GenericImportWarehouseSource.Mapping), parameters);

        Assert.Equal("DEL-EXCEL", rows[0].CoreFields[GenericImportLogicalField.Branch]);
        Assert.Equal("99", rows[0].CoreFields[GenericImportLogicalField.Warehouse]);
    }

    [Fact]
    public void En_compras_la_sucursal_no_aplica()
    {
        var rows = Rows();
        var parameters = new GenericImportParametersDto(GenericImportModule.Purchase, "PurchaseOrder", GenericImportLineType.Item, "P1",
            ShipToCode: "SUC-01", WarehouseCode: "01");

        GenericImportService.ApplyScreenValues(rows, Config(GenericImportModule.Purchase), parameters);

        Assert.Equal("DEL-EXCEL", rows[0].CoreFields[GenericImportLogicalField.Branch]);
        Assert.Equal("01", rows[0].CoreFields[GenericImportLogicalField.Warehouse]);
    }

    [Fact]
    public void Sucursal_en_pantalla_vacia_deja_la_del_cliente_por_defecto()
    {
        var rows = Rows();
        var parameters = new GenericImportParametersDto(GenericImportModule.Sales, "SalesOrder", GenericImportLineType.Item, "C1");

        GenericImportService.ApplyScreenValues(rows, Config(warehouse: GenericImportWarehouseSource.Mapping), parameters);

        // Sin ShipToCode el documento sale sin sucursal y SAP usa la de despacho por defecto del cliente.
        Assert.All(rows, r => Assert.Null(r.CoreFields[GenericImportLogicalField.Branch]));
    }

    [Fact]
    public void Validador_ignora_las_filas_pedidas_en_pantalla()
    {
        var ignored = GenericImportFormatValidator.IgnoredFields(Config());

        Assert.Contains(GenericImportLogicalField.Branch, ignored.Keys);
        Assert.Contains(GenericImportLogicalField.Warehouse, ignored.Keys);
        Assert.DoesNotContain(GenericImportLogicalField.Warehouse, GenericImportFormatValidator.RequiredFields(Config()));
    }

    [Fact]
    public async Task Guarda_sucursal_en_pantalla_y_conserva_el_campo_del_almacen_del_cliente()
    {
        await using var db = new PortalSaasDbContext(new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(nameof(Guarda_sucursal_en_pantalla_y_conserva_el_campo_del_almacen_del_cliente)).Options);
        var entity = new Data.Entities.GenericImportConfig
        {
            CompanyId = CompanyId, Module = "Sales", DocumentType = "SalesOrder", LineType = "Item", Alias = "Estándar",
        };
        db.GenericImportConfigs.Add(entity);
        await db.SaveChangesAsync();
        var service = new GenericImportConfigService(db, new FakeCurrentCompanyAccessor(CompanyId));

        await service.SaveDocumentOptionsAsync(entity.Id, new GenericImportDocumentOptionsDto(
            GenericImportWarehouseSource.Screen, null, BranchSource: GenericImportBranchSource.Screen));

        var options = (await service.GetAsync(entity.Id))!.DocumentOptions;
        Assert.Equal(GenericImportBranchSource.Screen, options.BranchSource);
        Assert.Equal(GenericImportWarehouseSource.Screen, options.WarehouseSource);
        // El campo precarga el selector de almacén -- se guarda el sugerido si vino vacío.
        Assert.Equal(GenericImportDocumentOptionsDto.DefaultWarehouseUserField, options.WarehouseUserField);
    }

    private sealed class FakeCurrentCompanyAccessor(Guid companyId) : ICurrentCompanyAccessor
    {
        public bool HasCompany => true;
        public Guid CompanyId => companyId;
        public string Code => "TEST";
        public string Database => throw new NotSupportedException();
        public string ServiceLayerUrl => throw new NotSupportedException();
        public string Country => throw new NotSupportedException();
    }
}
