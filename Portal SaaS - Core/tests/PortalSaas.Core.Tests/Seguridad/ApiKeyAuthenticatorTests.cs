using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Seguridad;

public class ApiKeyAuthenticatorTests
{
    private static PortalSaasDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PortalSaasDbContext(options);
    }

    private static async Task<Company> SembrarCompanyAsync(PortalSaasDbContext contexto)
    {
        var company = new Company
        {
            OrganizationId = Guid.NewGuid(),
            InstanceId = 1,
            Code = "DEPOR01",
            Name = "Comercial Depor",
            DatabaseName = "DEPOR_PRD",
            ServiceLayerUrl = "https://sap.example.com:50000/b1s/v1",
            IntegrationUsername = "integracion",
            IntegrationSecretKey = "no-usado-en-este-test",
            Country = "CL",
            IsActive = true,
        };
        contexto.Companies.Add(company);
        await contexto.SaveChangesAsync();
        return company;
    }

    [Fact]
    public async Task AuthenticateAsync_ConKeyValidaYActiva_RetornaDatosDeLaCompany()
    {
        await using var contexto = CrearContexto();
        var company = await SembrarCompanyAsync(contexto);
        var rawKey = ApiKeyGenerator.GenerateRawKey();
        contexto.ApiClientCredentials.Add(new ApiClientCredential
        {
            CompanyId = company.Id,
            Nombre = "Test",
            ApiKeyHash = ApiKeyGenerator.Hash(rawKey),
            Activo = true,
        });
        await contexto.SaveChangesAsync();

        var authenticator = new ApiKeyAuthenticator(contexto);
        var resultado = await authenticator.AuthenticateAsync(rawKey, CancellationToken.None);

        Assert.True(resultado.Success);
        Assert.Equal(company.Id, resultado.CompanyId);
        Assert.Equal("DEPOR01", resultado.CompanyCode);
        Assert.Equal("DEPOR_PRD", resultado.CompanyDatabase);
    }

    [Fact]
    public async Task AuthenticateAsync_ConKeyInexistente_RetornaFailure()
    {
        await using var contexto = CrearContexto();
        var authenticator = new ApiKeyAuthenticator(contexto);

        var resultado = await authenticator.AuthenticateAsync("clave-que-no-existe", CancellationToken.None);

        Assert.False(resultado.Success);
    }

    [Fact]
    public async Task AuthenticateAsync_ConKeyRevocada_RetornaFailure()
    {
        await using var contexto = CrearContexto();
        var company = await SembrarCompanyAsync(contexto);
        var rawKey = ApiKeyGenerator.GenerateRawKey();
        contexto.ApiClientCredentials.Add(new ApiClientCredential
        {
            CompanyId = company.Id,
            Nombre = "Test",
            ApiKeyHash = ApiKeyGenerator.Hash(rawKey),
            Activo = false,
        });
        await contexto.SaveChangesAsync();

        var authenticator = new ApiKeyAuthenticator(contexto);
        var resultado = await authenticator.AuthenticateAsync(rawKey, CancellationToken.None);

        Assert.False(resultado.Success);
    }

    [Fact]
    public async Task AuthenticateAsync_ConCompanyInactiva_RetornaFailure()
    {
        await using var contexto = CrearContexto();
        var company = await SembrarCompanyAsync(contexto);
        company.IsActive = false;
        await contexto.SaveChangesAsync();

        var rawKey = ApiKeyGenerator.GenerateRawKey();
        contexto.ApiClientCredentials.Add(new ApiClientCredential
        {
            CompanyId = company.Id,
            Nombre = "Test",
            ApiKeyHash = ApiKeyGenerator.Hash(rawKey),
            Activo = true,
        });
        await contexto.SaveChangesAsync();

        var authenticator = new ApiKeyAuthenticator(contexto);
        var resultado = await authenticator.AuthenticateAsync(rawKey, CancellationToken.None);

        Assert.False(resultado.Success);
    }

    [Fact]
    public async Task AuthenticateAsync_ConKeyValida_ActualizaLastUsedAt()
    {
        await using var contexto = CrearContexto();
        var company = await SembrarCompanyAsync(contexto);
        var rawKey = ApiKeyGenerator.GenerateRawKey();
        var credencial = new ApiClientCredential
        {
            CompanyId = company.Id,
            Nombre = "Test",
            ApiKeyHash = ApiKeyGenerator.Hash(rawKey),
            Activo = true,
        };
        contexto.ApiClientCredentials.Add(credencial);
        await contexto.SaveChangesAsync();

        var authenticator = new ApiKeyAuthenticator(contexto);
        await authenticator.AuthenticateAsync(rawKey, CancellationToken.None);

        var credencialActualizada = await contexto.ApiClientCredentials.FirstAsync(c => c.Id == credencial.Id);
        Assert.NotNull(credencialActualizada.LastUsedAt);
    }
}
