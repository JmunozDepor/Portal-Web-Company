using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using PortalSaas.Data;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

public sealed class GenericImportConfigServiceValidationRulesTests
{
    private static PortalSaasDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new PortalSaasDbContext(options);
    }

    private static async Task<int> SeedConfigAsync(PortalSaasDbContext db, Guid companyId)
    {
        var entity = new Data.Entities.GenericImportConfig
        {
            CompanyId = companyId,
            Module = "Sales",
            DocumentType = "SalesOrder",
            LineType = "Item",
            Alias = "Estándar",
        };
        db.GenericImportConfigs.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    [Fact]
    public async Task SaveValidationRulesAsync_reemplaza_el_set_completo()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SaveValidationRulesAsync_reemplaza_el_set_completo));
        var configId = await SeedConfigAsync(db, companyId);
        var service = new GenericImportConfigService(db, new FakeCurrentCompanyAccessor(companyId));

        await service.SaveValidationRulesAsync(configId,
        [
            new GenericImportValidationRuleAssignmentDto(0, GenericImportValidationRuleType.StockAvailable,
                GenericImportValidationSeverity.Warning, true, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }),
        ]);

        await service.SaveValidationRulesAsync(configId,
        [
            new GenericImportValidationRuleAssignmentDto(0, GenericImportValidationRuleType.CustomerActiveInSap,
                GenericImportValidationSeverity.Block, true, new Dictionary<string, object?>()),
        ]);

        var config = await service.GetAsync(configId);
        Assert.NotNull(config);
        var rule = Assert.Single(config!.ValidationRules);
        Assert.Equal(GenericImportValidationRuleType.CustomerActiveInSap, rule.RuleType);
    }

    [Fact]
    public async Task SaveValidationRulesAsync_parametros_vuelven_como_primitivos_convertibles()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SaveValidationRulesAsync_parametros_vuelven_como_primitivos_convertibles));
        var configId = await SeedConfigAsync(db, companyId);
        var service = new GenericImportConfigService(db, new FakeCurrentCompanyAccessor(companyId));

        await service.SaveValidationRulesAsync(configId,
        [
            new GenericImportValidationRuleAssignmentDto(0, GenericImportValidationRuleType.PriceVsFixedList,
                GenericImportValidationSeverity.Warning, true,
                new Dictionary<string, object?> { ["priceListNum"] = 2, ["tolerancePercent"] = 1.5m }),
        ]);

        var rule = Assert.Single((await service.GetAsync(configId))!.ValidationRules);

        // Las reglas hacen Convert.ToInt32/ToDecimal sobre estos valores -- no deben ser JsonElement.
        Assert.Equal(2, Convert.ToInt32(rule.Parameters["priceListNum"]));
        Assert.Equal(1.5m, Convert.ToDecimal(rule.Parameters["tolerancePercent"]));
    }

    [Fact]
    public async Task SaveValidationRulesAsync_rechaza_dos_reglas_de_precio_a_la_vez()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SaveValidationRulesAsync_rechaza_dos_reglas_de_precio_a_la_vez));
        var configId = await SeedConfigAsync(db, companyId);
        var service = new GenericImportConfigService(db, new FakeCurrentCompanyAccessor(companyId));

        var rules = new List<GenericImportValidationRuleAssignmentDto>
        {
            new(0, GenericImportValidationRuleType.PriceVsFixedList, GenericImportValidationSeverity.Warning, true,
                new Dictionary<string, object?> { ["priceListNum"] = 1, ["tolerancePercent"] = 0m }),
            new(0, GenericImportValidationRuleType.PriceVsCustomerList, GenericImportValidationSeverity.Warning, true,
                new Dictionary<string, object?> { ["tolerancePercent"] = 0m }),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveValidationRulesAsync(configId, rules));
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
