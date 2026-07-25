using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PortalSaas.Core.Seguridad;
using Xunit;

namespace PortalSaas.Core.Tests;

public class CurrentCompanyAccessorTests
{
    private static CurrentCompanyAccessor CrearConClaims(params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims)),
        };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new CurrentCompanyAccessor(accessor);
    }

    [Fact]
    public void CompanyId_ConClaim_ParseaElGuid()
    {
        var companyId = Guid.NewGuid();
        var accessor = CrearConClaims(new Claim("CompanyId", companyId.ToString()));

        Assert.Equal(companyId, accessor.CompanyId);
    }

    [Fact]
    public void Code_SinClaim_LanzaExcepcion()
    {
        var accessor = CrearConClaims();

        Assert.Throws<InvalidOperationException>(() => accessor.Code);
    }

    [Fact]
    public void TodasLasPropiedades_LeenSuClaimCorrespondiente()
    {
        var companyId = Guid.NewGuid();
        var accessor = CrearConClaims(
            new Claim("CompanyId", companyId.ToString()),
            new Claim("CompanyCode", "DEPOR"),
            new Claim("CompanyDatabase", "CLPRDDEPOR"),
            new Claim("CompanyServiceLayerUrl", "https://sap.local:50000/b1s/v1"),
            new Claim("CompanyCountry", "CL"));

        Assert.Equal(companyId, accessor.CompanyId);
        Assert.Equal("DEPOR", accessor.Code);
        Assert.Equal("CLPRDDEPOR", accessor.Database);
        Assert.Equal("https://sap.local:50000/b1s/v1", accessor.ServiceLayerUrl);
        Assert.Equal("CL", accessor.Country);
    }
}
