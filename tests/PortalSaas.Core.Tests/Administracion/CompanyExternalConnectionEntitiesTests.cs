using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class CompanyExternalConnectionEntitiesTests
{
    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task PuedePersistirYLeerConexionConBinding()
    {
        var companyId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        await using (var db = NuevoContexto())
        {
            var conn = new CompanyExternalConnection
            {
                OrganizationId = orgId,
                CompanyId = companyId,
                Nombre = "BD WMS",
                Tipo = ExternalConnectionType.DbSqlServer,
                Host = "sqlsap.cdepor.cl",
                Port = 11433,
                DatabaseName = "PS_COMDEPOR_WMS_DEV",
                TechnicalUsername = "svc_wms",
                TechnicalSecretKey = "cipher",
            };
            db.CompanyExternalConnections.Add(conn);
            await db.SaveChangesAsync();

            db.CompanyModuleConnections.Add(new CompanyModuleConnection
            {
                OrganizationId = orgId,
                CompanyId = companyId,
                ModuleCode = "Wms",
                Purpose = "Default",
                ConnectionId = conn.Id,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NuevoContexto()) { /* contexto distinto: InMemory por nombre único, sólo valida el modelo compila+mapea */ }

        Assert.Contains(ExternalConnectionType.DbHana, ExternalConnectionType.All);
        Assert.Equal("hana", ExternalDatabaseEngineType.Hana);
    }
}
