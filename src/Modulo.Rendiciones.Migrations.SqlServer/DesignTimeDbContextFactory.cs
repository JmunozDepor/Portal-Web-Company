using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulo.Rendiciones.Data;

namespace Modulo.Rendiciones.Migrations.SqlServer;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones SQL Server de
/// RendicionesDbContext. Nunca se usa en runtime real -- el plugin registra el
/// DbContext por DI vía IExternalDatabaseConnectionService (ver
/// ModuloRendiciones.RegisterServices, motor resuelto en runtime).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RendicionesDbContext>
{
    public RendicionesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("RENDICIONES_CONNECTION_STRING")
            ?? "Server=localhost;Database=modulo_rendiciones_dev;User Id=portalsaas;Password=portalsaas_dev_only;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseSqlServer(connectionString, x => x.MigrationsAssembly("Modulo.Rendiciones.Migrations.SqlServer"));

        return new RendicionesDbContext(optionsBuilder.Options);
    }
}
