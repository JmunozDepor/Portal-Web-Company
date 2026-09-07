using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Sap;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Sap;

public sealed class SapTraceabilityFieldResolverTests
{
    private sealed class CompanyAccessorFalso : ICurrentCompanyAccessor
    {
        private readonly Guid? _companyId;
        public CompanyAccessorFalso(Guid? companyId) => _companyId = companyId;

        public bool HasCompany => _companyId is not null;
        public Guid CompanyId => _companyId ?? throw new InvalidOperationException("sin compañía");
        public string Code => throw new NotSupportedException();
        public string Database => throw new NotSupportedException();
        public string ServiceLayerUrl => throw new NotSupportedException();
        public string Country => throw new NotSupportedException();
    }

    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Company NuevaCompania(Guid id, string? udf) => new()
    {
        Id = id,
        OrganizationId = Guid.NewGuid(),
        Code = "DEPOR",
        Name = "Depor",
        DatabaseName = "DEPOR_DB",
        ServiceLayerUrl = "https://sl.local",
        IntegrationUsername = "svc",
        IntegrationSecretKey = "enc",
        Country = "CL",
        TraceabilityUserUdfName = udf,
    };

    [Fact]
    public async Task SinCompaniaEnLaSesion_DevuelveElDefault()
    {
        await using var db = NuevoContexto();
        var resolver = new SapTraceabilityFieldResolver(new CompanyAccessorFalso(null), db);

        Assert.Equal("U_PortalUser", await resolver.ResolveUserFieldNameAsync());
    }

    [Fact]
    public async Task CompaniaSinUdfConfigurado_DevuelveElDefault()
    {
        var companyId = Guid.NewGuid();
        await using var db = NuevoContexto();
        db.Companies.Add(NuevaCompania(companyId, udf: null));
        await db.SaveChangesAsync();

        var resolver = new SapTraceabilityFieldResolver(new CompanyAccessorFalso(companyId), db);

        Assert.Equal("U_PortalUser", await resolver.ResolveUserFieldNameAsync());
    }

    [Fact]
    public async Task CompaniaConUdfConfigurado_DevuelveEseNombre()
    {
        var companyId = Guid.NewGuid();
        await using var db = NuevoContexto();
        db.Companies.Add(NuevaCompania(companyId, udf: "  U_DEP_PortalUsuario  "));
        await db.SaveChangesAsync();

        var resolver = new SapTraceabilityFieldResolver(new CompanyAccessorFalso(companyId), db);

        Assert.Equal("U_DEP_PortalUsuario", await resolver.ResolveUserFieldNameAsync());
    }

    [Fact]
    public void MergeTraceabilityUser_SinCamposPrevios_CreaElParConElUdf()
    {
        var merged = SapAdditionalFieldsHelper.MergeTraceabilityUser(null, "U_DEP_PortalUsuario", "jmunoz");

        Assert.Equal("jmunoz", Assert.Contains("U_DEP_PortalUsuario", merged));
    }

    [Fact]
    public void MergeTraceabilityUser_ConCampoDeUsuarioHomonimo_LaTrazabilidadGana()
    {
        var previos = new Dictionary<string, object?> { ["U_DEP_PortalUsuario"] = "valor-del-importador", ["U_Otro"] = 1 };

        var merged = SapAdditionalFieldsHelper.MergeTraceabilityUser(previos, "U_DEP_PortalUsuario", "jmunoz");

        Assert.Equal("jmunoz", merged["U_DEP_PortalUsuario"]);
        Assert.Equal(1, merged["U_Otro"]);
    }
}
