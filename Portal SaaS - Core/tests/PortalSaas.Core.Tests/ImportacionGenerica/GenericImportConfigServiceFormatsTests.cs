using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using PortalSaas.Data;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

/// <summary>Varios formatos por socio/estándar para la misma combinación, distinguidos por Alias.</summary>
public sealed class GenericImportConfigServiceFormatsTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static PortalSaasDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<int> SeedAsync(PortalSaasDbContext db, string alias, string? cardCode,
        bool isActive = true, string documentType = "InventoryTransferRequest")
    {
        var entity = new Data.Entities.GenericImportConfig
        {
            CompanyId = CompanyId,
            Module = "Inventory",
            DocumentType = documentType,
            LineType = "Item",
            BusinessPartnerCardCode = cardCode,
            Alias = alias,
            IsActive = isActive,
            PriceSource = GenericImportPriceSource.BusinessPartner.ToString(),
        };
        db.GenericImportConfigs.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    private static GenericImportConfigService Service(PortalSaasDbContext db) => new(db, new FakeCurrentCompanyAccessor(CompanyId));

    [Fact]
    public async Task ListAvailableAsync_lista_los_del_socio_primero_y_despues_los_estandar_activos()
    {
        await using var db = CreateDb(nameof(ListAvailableAsync_lista_los_del_socio_primero_y_despues_los_estandar_activos));
        await SeedAsync(db, "Estándar B", null);
        await SeedAsync(db, "Reposición", "C76");
        await SeedAsync(db, "Estándar A", null);
        await SeedAsync(db, "Push tiendas", "C76");
        await SeedAsync(db, "Inactivo", "C76", isActive: false);
        await SeedAsync(db, "De otro socio", "C99");
        await SeedAsync(db, "Otro tipo de documento", "C76", documentType: "StockTransfer");

        var formats = await Service(db).ListAvailableAsync(GenericImportModule.Inventory, "InventoryTransferRequest",
            GenericImportLineType.Item, "C76");

        Assert.Equal(["Push tiendas", "Reposición", "Estándar A", "Estándar B"], formats.Select(f => f.Alias));
    }

    [Fact]
    public async Task ListAvailableAsync_sin_socio_solo_lista_estandar()
    {
        await using var db = CreateDb(nameof(ListAvailableAsync_sin_socio_solo_lista_estandar));
        await SeedAsync(db, "Estándar", null);
        await SeedAsync(db, "Del socio", "C76");

        var formats = await Service(db).ListAvailableAsync(GenericImportModule.Inventory, "InventoryTransferRequest",
            GenericImportLineType.Item, null);

        Assert.Equal(["Estándar"], formats.Select(f => f.Alias));
    }

    [Fact]
    public async Task ResolveAsync_con_configId_devuelve_el_formato_elegido()
    {
        await using var db = CreateDb(nameof(ResolveAsync_con_configId_devuelve_el_formato_elegido));
        await SeedAsync(db, "A primero", "C76");
        var elegido = await SeedAsync(db, "B elegido", "C76");

        var config = await Service(db).ResolveAsync(GenericImportModule.Inventory, "InventoryTransferRequest",
            GenericImportLineType.Item, "C76", elegido);

        Assert.Equal("B elegido", config?.Alias);
    }

    [Fact]
    public async Task ResolveAsync_con_configId_de_otro_socio_devuelve_null()
    {
        await using var db = CreateDb(nameof(ResolveAsync_con_configId_de_otro_socio_devuelve_null));
        var ajeno = await SeedAsync(db, "Formato ajeno", "C99");

        var config = await Service(db).ResolveAsync(GenericImportModule.Inventory, "InventoryTransferRequest",
            GenericImportLineType.Item, "C76", ajeno);

        Assert.Null(config);
    }

    [Fact]
    public async Task ResolveAsync_sin_configId_prefiere_el_del_socio_y_si_no_el_estandar()
    {
        await using var db = CreateDb(nameof(ResolveAsync_sin_configId_prefiere_el_del_socio_y_si_no_el_estandar));
        await SeedAsync(db, "Estándar", null);
        await SeedAsync(db, "Z del socio", "C76");
        await SeedAsync(db, "A del socio", "C76");
        var service = Service(db);

        var delSocio = await service.ResolveAsync(GenericImportModule.Inventory, "InventoryTransferRequest", GenericImportLineType.Item, "C76");
        var sinExcepcion = await service.ResolveAsync(GenericImportModule.Inventory, "InventoryTransferRequest", GenericImportLineType.Item, "C55");

        Assert.Equal("A del socio", delSocio?.Alias);
        Assert.Equal("Estándar", sinExcepcion?.Alias);
    }

    [Fact]
    public async Task CreateAsync_rechaza_alias_repetido_en_la_misma_combinacion_y_socio()
    {
        await using var db = CreateDb(nameof(CreateAsync_rechaza_alias_repetido_en_la_misma_combinacion_y_socio));
        await SeedAsync(db, "Push tiendas", "C76");
        var service = Service(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(GenericImportModule.Inventory,
            "InventoryTransferRequest", GenericImportLineType.Item, "C76", null, GenericImportItemIdentifier.ItemCode, " Push tiendas ", []));

        // Mismo alias para OTRO socio sí se permite.
        var otro = await service.CreateAsync(GenericImportModule.Inventory,
            "InventoryTransferRequest", GenericImportLineType.Item, "C99", null, GenericImportItemIdentifier.ItemCode, "Push tiendas", []);
        Assert.True(otro > 0);
    }

    private sealed class FakeCurrentCompanyAccessor : ICurrentCompanyAccessor
    {
        private readonly Guid _companyId;
        public FakeCurrentCompanyAccessor(Guid companyId) => _companyId = companyId;

        public bool HasCompany => true;
        public Guid CompanyId => _companyId;
        public string Code => "TEST";
        public string Database => throw new NotSupportedException();
        public string ServiceLayerUrl => throw new NotSupportedException();
        public string Country => throw new NotSupportedException();
    }
}
