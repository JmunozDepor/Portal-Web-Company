using Microsoft.Data.SqlClient;
using Npgsql;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

public sealed class ExternalDatabaseConnectionStringBuilderTests
{
    [Fact]
    public void Postgres_ArmaHostPuertoBaseYUsuario()
    {
        var cs = ExternalDatabaseConnectionStringBuilder.Build(
            InstanceEngineType.Postgres, "pg.local", 5432, "rendiciones", "app", "secreto");

        var b = new NpgsqlConnectionStringBuilder(cs);
        Assert.Equal("pg.local", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("rendiciones", b.Database);
        Assert.Equal("app", b.Username);
        Assert.Equal("secreto", b.Password);
    }

    [Fact]
    public void SqlServer_UsaHostComaPuertoYConfiaEnElCertificado()
    {
        var cs = ExternalDatabaseConnectionStringBuilder.Build(
            InstanceEngineType.SqlServer, "sql.local", 1433, "gastos", "app", "secreto", timeoutSeconds: 5);

        var b = new SqlConnectionStringBuilder(cs);
        Assert.Equal("sql.local,1433", b.DataSource);
        Assert.Equal("gastos", b.InitialCatalog);
        Assert.Equal("app", b.UserID);
        Assert.True(b.TrustServerCertificate);
        Assert.Equal(5, b.ConnectTimeout);
    }

    [Fact]
    public void Hana_UsaLaBaseComoCurrentSchema()
    {
        var cs = ExternalDatabaseConnectionStringBuilder.Build(
            InstanceEngineType.Hana, "hana.local", 30015, "SBO_DEPOR", "app", "secreto");

        Assert.Equal("Server=hana.local:30015;UserID=app;Password=secreto;Current Schema=SBO_DEPOR", cs);
    }

    [Fact]
    public void MotorDesconocido_Lanza()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ExternalDatabaseConnectionStringBuilder.Build("oracle", "h", 1, "d", "u", "p"));
        Assert.Contains("oracle", ex.Message);
    }

    [Fact]
    public void LosMotoresDeInstanciaCoincidenConLosDelContratoDePlugins()
    {
        Assert.Equal(ExternalDatabaseEngineType.Postgres, InstanceEngineType.Postgres);
        Assert.Equal(ExternalDatabaseEngineType.SqlServer, InstanceEngineType.SqlServer);
        Assert.Equal(ExternalDatabaseEngineType.Hana, InstanceEngineType.Hana);
    }

    [Fact]
    public void Sap_SoloIncluyeHanaYSqlServer()
    {
        Assert.True(InstanceEngineType.IsSap(InstanceEngineType.Hana));
        Assert.True(InstanceEngineType.IsSap(InstanceEngineType.SqlServer));
        Assert.False(InstanceEngineType.IsSap(InstanceEngineType.Postgres));
    }
}
