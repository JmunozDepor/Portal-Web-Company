using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulo.Wms.Data;

namespace Modulo.Wms.Migrations.Postgres;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones Postgres de WmsDbContext.
/// Nunca se usa en runtime real -- el plugin registra el DbContext por DI vía
/// IExternalDatabaseConnectionService (ver ModuloWms.RegisterServices, motor resuelto
/// en runtime). Mismo patrón que Modulo.Rendiciones.Migrations.Postgres.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WmsDbContext>
{
    public WmsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("WMS_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=modulo_wms_dev;Username=portalsaas;Password=portalsaas_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<WmsDbContext>()
            .UseNpgsql(connectionString, x => x.MigrationsAssembly("Modulo.Wms.Migrations.Postgres"));

        return new WmsDbContext(optionsBuilder.Options);
    }
}
