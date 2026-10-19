using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using PortalSaas.Data;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

/// <summary>Opciones del documento por formato: origen del almacén y modo del vendedor (2026-10-15).</summary>
public sealed class GenericImportDocumentOptionsTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Theory]
    [InlineData(GenericImportSalesPersonMode.BusinessPartner, 7, 3, 3)]
    [InlineData(GenericImportSalesPersonMode.Fixed, 7, 3, 9)]
    [InlineData(GenericImportSalesPersonMode.MultiSeller, 7, 3, 7)]
    [InlineData(GenericImportSalesPersonMode.MultiSeller, null, 3, 3)]
    public void Vendedor_segun_el_modo_del_formato(GenericImportSalesPersonMode mode, int? chosen, int? partnerDefault, int? expected)
    {
        var options = new GenericImportDocumentOptionsDto(SalesPersonMode: mode, SalesPersonCode: 9);

        Assert.Equal(expected, GenericImportService.ResolveSalesPerson(options, chosen, partnerDefault));
    }

    [Fact]
    public void Fijo_ignora_lo_elegido_al_importar()
    {
        var options = new GenericImportDocumentOptionsDto(SalesPersonMode: GenericImportSalesPersonMode.Fixed, SalesPersonCode: 9);

        Assert.Equal(9, GenericImportService.ResolveSalesPerson(options, chosenInWizard: 7, partnerDefault: 3));
    }

    private static async Task<(PortalSaasDbContext Db, GenericImportConfigService Service, int Id)> SeedAsync(string name)
    {
        var db = new PortalSaasDbContext(new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(name).Options);
        var entity = new Data.Entities.GenericImportConfig
        {
            CompanyId = CompanyId, Module = "Sales", DocumentType = "SalesOrder", LineType = "Item", Alias = "Prueba",
        };
        db.GenericImportConfigs.Add(entity);
        await db.SaveChangesAsync();
        return (db, new GenericImportConfigService(db, new FakeCurrentCompanyAccessor(CompanyId)), entity.Id);
    }

    [Fact]
    public async Task Guarda_y_devuelve_las_opciones()
    {
        var (db, service, id) = await SeedAsync(nameof(Guarda_y_devuelve_las_opciones));
        await using var _ = db;

        await service.SaveDocumentOptionsAsync(id, new GenericImportDocumentOptionsDto(
            GenericImportWarehouseSource.BusinessPartnerField, " U_NX_WhsCode ", GenericImportSalesPersonMode.Fixed, 12));

        var options = (await service.GetAsync(id))!.DocumentOptions;
        Assert.Equal(GenericImportWarehouseSource.BusinessPartnerField, options.WarehouseSource);
        Assert.Equal("U_NX_WhsCode", options.WarehouseUserField);
        Assert.Equal(GenericImportSalesPersonMode.Fixed, options.SalesPersonMode);
        Assert.Equal(12, options.SalesPersonCode);
    }

    [Theory]
    [InlineData("NX_WhsCode")]
    [InlineData("U_NX WhsCode")]
    [InlineData("U_X\"; DROP")]
    public async Task Rechaza_un_campo_de_usuario_invalido(string userField)
    {
        var (db, service, id) = await SeedAsync(nameof(Rechaza_un_campo_de_usuario_invalido) + userField);
        await using var _ = db;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDocumentOptionsAsync(id,
            new GenericImportDocumentOptionsDto(GenericImportWarehouseSource.BusinessPartnerField, userField)));
    }

    [Fact]
    public async Task Vendedor_fijo_sin_vendedor_se_rechaza()
    {
        var (db, service, id) = await SeedAsync(nameof(Vendedor_fijo_sin_vendedor_se_rechaza));
        await using var _ = db;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDocumentOptionsAsync(id,
            new GenericImportDocumentOptionsDto(SalesPersonMode: GenericImportSalesPersonMode.Fixed)));
    }

    [Fact]
    public async Task Formato_sin_opciones_guardadas_usa_los_valores_de_siempre()
    {
        var (db, service, id) = await SeedAsync(nameof(Formato_sin_opciones_guardadas_usa_los_valores_de_siempre));
        await using var _ = db;

        var options = (await service.GetAsync(id))!.DocumentOptions;
        Assert.Equal(GenericImportWarehouseSource.Mapping, options.WarehouseSource);
        Assert.Equal(GenericImportSalesPersonMode.BusinessPartner, options.SalesPersonMode);
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
