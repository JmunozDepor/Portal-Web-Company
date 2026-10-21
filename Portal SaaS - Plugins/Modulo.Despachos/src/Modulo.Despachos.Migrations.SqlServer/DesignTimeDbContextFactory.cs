using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulo.Despachos.Data;

namespace Modulo.Despachos.Migrations.SqlServer;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones SQL Server de
/// DespachosDbContext. Nunca se usa en runtime real -- el plugin registra el
/// DbContext por DI vía IExternalDatabaseConnectionService (ver
/// ModuloDespachos.RegisterServices, motor resuelto en runtime).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DespachosDbContext>
{
    public DespachosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DESPACHOS_CONNECTION_STRING")
            ?? "Server=localhost;Database=modulo_despachos_dev;User Id=portalsaas;Password=portalsaas_dev_only;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<DespachosDbContext>()
            .UseSqlServer(connectionString, x => x.MigrationsAssembly("Modulo.Despachos.Migrations.SqlServer"));

        return new DespachosDbContext(optionsBuilder.Options);
    }
}

