using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulo.Rendiciones.Data;

namespace Modulo.Rendiciones.Migrations.Postgres;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones Postgres de
/// RendicionesDbContext. Nunca se usa en runtime real -- el plugin registra el
/// DbContext por DI vía IExternalDatabaseConnectionService (ver
/// ModuloRendiciones.RegisterServices, motor resuelto en runtime).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RendicionesDbContext>
{
    public RendicionesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("RENDICIONES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=modulo_rendiciones_dev;Username=portalsaas;Password=portalsaas_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseNpgsql(connectionString, x => x.MigrationsAssembly("Modulo.Rendiciones.Migrations.Postgres"));

        return new RendicionesDbContext(optionsBuilder.Options);
    }
}
