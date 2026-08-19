using Microsoft.Extensions.Options;
using Servicios.Common.Configuracion;
using Servicios.Common.Contratos;
using Xunit;

namespace Servicios.Common.Tests;

public class AppSettingsCompanyProviderTests
{
    [Fact]
    public async Task GetActiveCompaniesAsync_ExcluyeCompaniasInactivas()
    {
        var sociedades = new List<SociedadSetting>
        {
            NuevaSociedad("DEPOR", isActive: true),
            NuevaSociedad("INACTIVA", isActive: false)
        };
        var provider = new AppSettingsCompanyProvider(Options.Create(sociedades));

        var activas = await provider.GetActiveCompaniesAsync(CancellationToken.None);

        Assert.Single(activas);
        Assert.Equal("DEPOR", activas[0].CompanyCode);
    }

    [Fact]
    public async Task GetActiveCompaniesAsync_ResuelveMotorSqlServerPorNombre()
    {
        var sociedades = new List<SociedadSetting> { NuevaSociedad("BLOCK_QA", isActive: true, engineType: "SqlServer") };
        var provider = new AppSettingsCompanyProvider(Options.Create(sociedades));

        var activas = await provider.GetActiveCompaniesAsync(CancellationToken.None);

        Assert.Equal(MotorBaseDatos.SqlServer, activas[0].EngineType);
    }

    [Fact]
    public async Task GetActiveCompaniesAsync_PropagaVistaProcedimientoYUdfConfigurados()
    {
        var sociedades = new List<SociedadSetting>
        {
            new()
            {
                CompanyCode = "DEPOR",
                EngineType = "Hana",
                Host = "host",
                Port = 30015,
                Schema = "SCHEMA",
                DbUserId = "usuario",
                DbSecreto = "secreto-cifrado",
                ServiceLayerUrl = "https://sl.local/",
                ServiceLayerUsername = "sl-usuario",
                ServiceLayerSecreto = "secreto-cifrado-sl",
                HeaderQuerySource = "\"_SYS_BIC\".\"sap.clprddepor/NX_AUTO_ABS\"",
                WarehouseAssignmentProcedure = "SP_DEP_ORDER_ABS",
                CompletionUdfFieldName = "U_NX_Auto_ABS",
                IsActive = true
            }
        };
        var provider = new AppSettingsCompanyProvider(Options.Create(sociedades));

        var activas = await provider.GetActiveCompaniesAsync(CancellationToken.None);

        Assert.Equal("\"_SYS_BIC\".\"sap.clprddepor/NX_AUTO_ABS\"", activas[0].HeaderQuerySource);
        Assert.Equal("SP_DEP_ORDER_ABS", activas[0].WarehouseAssignmentProcedure);
        Assert.Equal("U_NX_Auto_ABS", activas[0].CompletionUdfFieldName);
    }

    private static SociedadSetting NuevaSociedad(string companyCode, bool isActive, string engineType = "Hana") => new()
    {
        CompanyCode = companyCode,
        EngineType = engineType,
        Host = "host",
        Port = 30015,
        Schema = "SCHEMA",
        DbUserId = "usuario",
        DbSecreto = "secreto-cifrado",
        ServiceLayerUrl = "https://sl.local/",
        ServiceLayerUsername = "sl-usuario",
        ServiceLayerSecreto = "secreto-cifrado-sl",
        HeaderQuerySource = "vista-cabecera",
        WarehouseAssignmentProcedure = "sp-asignacion-bodegas",
        CompletionUdfFieldName = "U_Completado",
        IsActive = isActive
    };
}
