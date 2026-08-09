using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulo.Wms.Data;

namespace Modulo.Wms.Migrations.SqlServer;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones SQL Server de WmsDbContext.
/// Nunca se usa en runtime real -- el plugin registra el DbContext por DI vía
/// IExternalDatabaseConnectionService (ver ModuloWms.RegisterServices, motor resuelto
/// en runtime). Mismo patrón que Modulo.Rendiciones.Migrations.SqlServer.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WmsDbContext>
{
    public WmsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("WMS_CONNECTION_STRING")
            ?? "Server=localhost;Database=modulo_wms_dev;User Id=portalsaas;Password=portalsaas_dev_only;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer(connectionString, x => x.MigrationsAssembly("Modulo.Wms.Migrations.SqlServer"));

        return new WmsDbContext(optionsBuilder.Options);
    }
}
