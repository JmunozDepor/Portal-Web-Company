using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulo.Despachos.Data;

namespace Modulo.Despachos.Migrations.Postgres;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones Postgres de
/// DespachosDbContext. Nunca se usa en runtime real -- el plugin registra el
/// DbContext por DI vía IExternalDatabaseConnectionService (ver
/// ModuloDespachos.RegisterServices, motor resuelto en runtime).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DespachosDbContext>
{
    public DespachosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DESPACHOS_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=modulo_despachos_dev;Username=portalsaas;Password=portalsaas_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<DespachosDbContext>()
            .UseNpgsql(connectionString, x => x.MigrationsAssembly("Modulo.Despachos.Migrations.Postgres"));

        return new DespachosDbContext(optionsBuilder.Options);
    }
}

