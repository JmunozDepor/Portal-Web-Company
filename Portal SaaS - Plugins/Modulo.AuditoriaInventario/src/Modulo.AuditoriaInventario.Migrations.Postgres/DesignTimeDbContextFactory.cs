using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulo.AuditoriaInventario.Data;

namespace Modulo.AuditoriaInventario.Migrations.Postgres;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones Postgres de
/// AuditoriaInventarioDbContext. Nunca se usa en runtime real -- el plugin registra
/// el DbContext por DI vía IExternalDatabaseConnectionService (ver
/// AuditoriaInventarioModule.RegisterServices, motor resuelto en runtime), mismo
/// criterio que Modulo.Rendiciones.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuditoriaInventarioDbContext>
{
    public AuditoriaInventarioDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUDITORIAINVENTARIO_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=modulo_auditoria_inventario_dev;Username=portalsaas;Password=portalsaas_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<AuditoriaInventarioDbContext>()
            .UseNpgsql(connectionString, x => x.MigrationsAssembly("Modulo.AuditoriaInventario.Migrations.Postgres"));

        return new AuditoriaInventarioDbContext(optionsBuilder.Options);
    }
}
